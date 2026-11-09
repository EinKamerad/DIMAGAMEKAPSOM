using System;
using System.Collections.Generic;
using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;

namespace BioFarme.Grid
{
    /// <summary>
    /// Pooled visual pulse droplet gliding along organic VeinBiomass / VeinPlasma / VeinArtery conduits.
    /// </summary>
    public sealed class PacketVisual
    {
        public GameObject GameObject;
        public Transform Transform;
        public SpriteRenderer Renderer;
        public readonly List<Vector3> Waypoints = new List<Vector3>(16);

        public ResourceType Resource;
        public float Amount;
        public Vector2Int TargetNodeAnchor;
        public IGridEntity TargetEntity;
        public int CurrentSegmentIndex;
        public float SegmentProgress;
        public float SpeedTilesPerSec;
        public bool Active;
    }

    /// <summary>
    /// Mindustry-style symbiotic vascular conduit network.
    /// Connects producers (PulseBulbGenerator, SymbioticHarvester, BioReactor, LivingHeartCore)
    /// across VeinBiomass / VeinPlasma / VeinArtery tiles to consumers (Core, Reactors, Turrets, Tendrils, Incubators),
    /// auto-irrigates adjacent farmland near VeinPlasma/VeinArtery, and animates glowing resource packets!
    /// </summary>
    public sealed class VeinNetworkSystem : MonoBehaviour
    {
        private const int InitialPacketPool = 36;
        private const int MaxActivePackets = 80;
        private const float NodeBufferCapacity = 12f;

        private sealed class ConnectedCluster
        {
            public readonly HashSet<Vector2Int> ConduitCells = new HashSet<Vector2Int>();
            public readonly List<IGridEntity> Producers = new List<IGridEntity>();
            public readonly List<IGridEntity> Consumers = new List<IGridEntity>();
            public float AverageSpeed = 3.2f;
            public int HighestConduitTier = 1;
            public int RoundRobinCursor;
        }

        private static VeinNetworkSystem _instance;

        public static VeinNetworkSystem Instance
        {
            get
            {
                if (_instance == null && BioGridManager.InstanceIfExists != null)
                {
                    _instance = BioGridManager.InstanceIfExists.GetComponent<VeinNetworkSystem>();
                    if (_instance == null)
                    {
                        _instance = BioGridManager.InstanceIfExists.gameObject.AddComponent<VeinNetworkSystem>();
                    }
                }
                return _instance;
            }
        }

        private Transform _packetsRoot;
        private readonly List<ConnectedCluster> _clusters = new List<ConnectedCluster>();
        private readonly List<Vector2Int> _irrigatingConduits = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> _connectedNodeAnchors = new HashSet<Vector2Int>();
        private readonly Dictionary<(Vector2Int anchor, ResourceType res), float> _nodeBuffers =
            new Dictionary<(Vector2Int anchor, ResourceType res), float>();

        private readonly Queue<PacketVisual> _packetPool = new Queue<PacketVisual>(InitialPacketPool);
        private readonly List<PacketVisual> _activePackets = new List<PacketVisual>(InitialPacketPool);

        // BFS scratch buffers for shortest path extraction along veins
        private readonly Queue<Vector2Int> _bfsQueue = new Queue<Vector2Int>(128);
        private readonly Dictionary<Vector2Int, Vector2Int> _bfsParent = new Dictionary<Vector2Int, Vector2Int>(128);

        private bool _topologyDirty = true;
        private float _irrigationTimer;
        private float _dispatchTimer;

        public int ActivePacketCount => _activePackets.Count;
        public int ClusterCount => _clusters.Count;

        private static readonly Vector2Int[] CardinalDirs =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }

            GameObject rootGo = new GameObject("VeinPackets");
            rootGo.transform.SetParent(transform, false);
            _packetsRoot = rootGo.transform;

            PrewarmPacketPool(InitialPacketPool);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            if (_topologyDirty)
            {
                RebuildNow();
            }

            float simDt = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.SimulationDeltaTime
                : Time.deltaTime;

            if (simDt <= 0f) return;

            // 1. Auto-irrigate TilledBed tiles adjacent to VeinPlasma and VeinArtery
            _irrigationTimer += simDt;
            if (_irrigationTimer >= 0.45f)
            {
                _irrigationTimer = 0f;
                PerformVeinAutoIrrigation();
            }

            // 2. Dispatch resource pulses from producers & Heart Core according to NetworkPriorityMode
            _dispatchTimer += simDt;
            if (_dispatchTimer >= 0.65f)
            {
                _dispatchTimer = 0f;
                DispatchClusterPackets();
            }

            // 3. Advance visual packets along vein waypoints
            TickActivePackets(simDt);
        }

        public void MarkDirty()
        {
            _topologyDirty = true;
        }

        /// <summary>
        /// Rebuilds connected conduit subnetworks and discovers all linked Producer and Consumer structures.
        /// </summary>
        public void RebuildNow()
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return;

            _topologyDirty = false;
            _clusters.Clear();
            _irrigatingConduits.Clear();
            _connectedNodeAnchors.Clear();

            bool[,] visitedConduit = new bool[BioGridManager.Width, BioGridManager.Height];

            for (int x = 0; x < BioGridManager.Width; x++)
            {
                for (int y = 0; y < BioGridManager.Height; y++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    StructureType st = grid.GetStructureTypeAt(cell);
                    if (!IsConduitType(st)) continue;

                    if (st == StructureType.VeinPlasma || st == StructureType.VeinArtery)
                    {
                        _irrigatingConduits.Add(cell);
                    }

                    if (visitedConduit[x, y]) continue;

                    ConnectedCluster cluster = BuildClusterFromSeed(grid, cell, visitedConduit);
                    if (cluster.ConduitCells.Count > 0)
                    {
                        _clusters.Add(cluster);
                    }
                }
            }

            // Perform immediate irrigation pass when plasma veins are placed
            PerformVeinAutoIrrigation();
        }

        private ConnectedCluster BuildClusterFromSeed(BioGridManager grid, Vector2Int seed, bool[,] visited)
        {
            ConnectedCluster cluster = new ConnectedCluster();
            HashSet<IGridEntity> seenProducers = new HashSet<IGridEntity>();
            HashSet<IGridEntity> seenConsumers = new HashSet<IGridEntity>();

            _bfsQueue.Clear();
            _bfsQueue.Enqueue(seed);
            visited[seed.x, seed.y] = true;

            float speedAccum = 0f;
            int conduitCount = 0;

            while (_bfsQueue.Count > 0)
            {
                Vector2Int cur = _bfsQueue.Dequeue();
                cluster.ConduitCells.Add(cur);
                conduitCount++;

                StructureType cType = grid.GetStructureTypeAt(cur);
                int tier = GetConduitTier(cType);
                cluster.HighestConduitTier = Mathf.Max(cluster.HighestConduitTier, tier);
                speedAccum += GetConduitPacketSpeed(tier);

                for (int d = 0; d < CardinalDirs.Length; d++)
                {
                    Vector2Int nb = cur + CardinalDirs[d];
                    if (!grid.IsInBounds(nb)) continue;

                    IGridEntity nbEntity = grid.GetStructureAt(nb);
                    if (nbEntity == null) continue;

                    if (IsConduitType(nbEntity.Type))
                    {
                        if (!visited[nb.x, nb.y])
                        {
                            visited[nb.x, nb.y] = true;
                            _bfsQueue.Enqueue(nb);
                        }
                    }
                    else
                    {
                        _connectedNodeAnchors.Add(nbEntity.GridPos);

                        if (ActsAsProducer(nbEntity) && seenProducers.Add(nbEntity))
                        {
                            cluster.Producers.Add(nbEntity);
                        }
                        if (ActsAsConsumer(nbEntity) && seenConsumers.Add(nbEntity))
                        {
                            cluster.Consumers.Add(nbEntity);
                        }
                    }
                }
            }

            cluster.AverageSpeed = conduitCount > 0 ? (speedAccum / conduitCount) : 3.2f;
            return cluster;
        }

        private void PerformVeinAutoIrrigation()
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null || _irrigatingConduits.Count == 0) return;

            for (int i = 0; i < _irrigatingConduits.Count; i++)
            {
                Vector2Int c = _irrigatingConduits[i];
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        Vector2Int target = new Vector2Int(c.x + dx, c.y + dy);
                        if (grid.CanWater(target))
                        {
                            grid.WaterTile(target, 1.0f);
                        }
                    }
                }
            }
        }

        private void DispatchClusterPackets()
        {
            if (_clusters.Count == 0 || _activePackets.Count >= MaxActivePackets) return;

            NetworkPriorityMode priority = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.CurrentPriority
                : NetworkPriorityMode.Balanced;

            for (int cIdx = 0; cIdx < _clusters.Count; cIdx++)
            {
                ConnectedCluster cluster = _clusters[cIdx];
                if (cluster.ConduitCells.Count == 0) continue;

                // Ambient arterial circulation pulse on starter veins connected to Heart Core even before turrets are built!
                if (cluster.Consumers.Count <= 1 && cluster.Producers.Count > 0 && cluster.ConduitCells.Count >= 2)
                {
                    MaybeSpawnAmbientCirculationPulse(cluster);
                }

                if (cluster.Producers.Count == 0 || cluster.Consumers.Count == 0) continue;

                for (int pIdx = 0; pIdx < cluster.Producers.Count; pIdx++)
                {
                    if (_activePackets.Count >= MaxActivePackets) return;

                    IGridEntity producer = cluster.Producers[pIdx];
                    if (producer == null) continue;

                    if (TrySelectBestConsumerAndPayload(cluster, producer, priority,
                        out IGridEntity chosenConsumer, out ResourceType payloadRes, out float payloadAmt))
                    {
                        SpawnPacketAlongCluster(cluster, producer, chosenConsumer, payloadRes, payloadAmt);
                    }
                }
            }
        }

        private void MaybeSpawnAmbientCirculationPulse(ConnectedCluster cluster)
        {
            // Light visual pulse along dead-end starter veins so the starter network visibly breathes on second 1!
            if (_activePackets.Count > 16 || UnityEngine.Random.value > 0.35f) return;

            IGridEntity src = cluster.Producers[0];
            if (src == null) return;

            Vector2Int farthestCell = default;
            float bestDist = -1f;
            foreach (Vector2Int cell in cluster.ConduitCells)
            {
                float d = Vector2.SqrMagnitude(cell - src.GridPos);
                if (d > bestDist)
                {
                    bestDist = d;
                    farthestCell = cell;
                }
            }

            if (bestDist > 0.5f)
            {
                ResourceType pulseType = cluster.HighestConduitTier >= 2 ? ResourceType.BioPlasma : ResourceType.Biomass;
                SpawnVisualPulseToCell(cluster, src, farthestCell, pulseType);
            }
        }

        private bool TrySelectBestConsumerAndPayload(
            ConnectedCluster cluster,
            IGridEntity producer,
            NetworkPriorityMode priorityMode,
            out IGridEntity bestConsumer,
            out ResourceType chosenResource,
            out float chosenAmount)
        {
            bestConsumer = null;
            chosenResource = ResourceType.Biomass;
            chosenAmount = 0f;

            int count = cluster.Consumers.Count;
            if (count == 0) return false;

            float bestScore = -1f;
            int startOffset = cluster.RoundRobinCursor % count;

            for (int step = 0; step < count; step++)
            {
                int idx = (startOffset + step) % count;
                IGridEntity consumer = cluster.Consumers[idx];
                if (consumer == null || ReferenceEquals(consumer, producer)) continue;

                if (!TryDetermineTransfer(producer, consumer, cluster.HighestConduitTier, out ResourceType res, out float amt))
                {
                    continue;
                }

                float weight = ComputeConsumerPriorityWeight(consumer, priorityMode);
                // Add slight round-robin tie-breaker so equal-priority turrets share supply evenly
                float score = weight + (count - step) * 0.02f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestConsumer = consumer;
                    chosenResource = res;
                    chosenAmount = amt;
                }
            }

            if (bestConsumer == null) return false;

            cluster.RoundRobinCursor = (cluster.RoundRobinCursor + 1) % count;
            return ExtractPayloadFromProducer(producer, chosenResource, ref chosenAmount);
        }

        private bool TryDetermineTransfer(
            IGridEntity producer,
            IGridEntity consumer,
            int maxConduitTier,
            out ResourceType res,
            out float amount)
        {
            res = ResourceType.Biomass;
            amount = 1f;

            // Case 1: Explicit IResourceProducer -> IResourceConsumer or Core
            if (producer is IResourceProducer customProd)
            {
                if (customProd.StoredOutputAmount < 0.25f) return false;
                res = customProd.OutputResourceType;
                amount = Mathf.Min(2f, customProd.StoredOutputAmount);

                if (consumer.Type == StructureType.LivingHeartCore) return true;
                if (consumer is IResourceConsumer customCons) return customCons.CanAcceptResource(res);
                return ConsumerNeedsResource(consumer, res);
            }

            // Case 2: LivingHeartCore acting as arterial pump supplying connected turrets, tendrils, reactors, incubators!
            if (producer.Type == StructureType.LivingHeartCore)
            {
                if (consumer.Type == StructureType.LivingHeartCore) return false;
                if (!GetRequiredSupplyResource(consumer, out res)) return false;

                // Basic VeinBiomass (tier 1) carries Biomass & Spores; BioPlasma requires VeinPlasma/VeinArtery OR short pressure!
                if (res == ResourceType.BioPlasma && maxConduitTier < 1) return false;

                if (consumer is IResourceConsumer consInterface && !consInterface.CanAcceptResource(res))
                {
                    return false;
                }

                float buffered = GetNodeBufferedAmount(consumer.GridPos, res);
                if (buffered >= NodeBufferCapacity * 0.85f) return false;

                ResourceManager rm = ResourceManager.InstanceIfExists;
                if (rm == null || rm.GetRaw(res) < 2f) return false;

                amount = Mathf.Min(2f, rm.GetRaw(res) * 0.25f);
                amount = Mathf.Max(0.5f, Mathf.Round(amount * 2f) * 0.5f);
                return rm.CanAfford(res, amount);
            }

            // Case 3: Built-in / standard StructureDefinition producer (PulseBulbGenerator, SymbioticHarvester, BioReactor)
            StructureDefinition pDef = BalanceDatabase.GetStructure(producer.Type);
            if (pDef == null || pDef.outputPerCycle <= 0f) return false;

            res = pDef.producedResource;
            float stored = GetNodeBufferedAmount(producer.GridPos, res);
            if (stored < 0.5f) return false;

            if (consumer.Type == StructureType.LivingHeartCore)
            {
                amount = Mathf.Min(3f, stored);
                return true;
            }

            if (consumer is IResourceConsumer ic)
            {
                if (!ic.CanAcceptResource(res)) return false;
                amount = Mathf.Min(2f, stored);
                return true;
            }

            if (ConsumerNeedsResource(consumer, res))
            {
                amount = Mathf.Min(2f, stored);
                return true;
            }

            return false;
        }

        private bool ExtractPayloadFromProducer(IGridEntity producer, ResourceType res, ref float amount)
        {
            if (producer is IResourceProducer customProd)
            {
                float extracted = customProd.ExtractOutput(amount);
                if (extracted <= 0.01f) return false;
                amount = extracted;
                return true;
            }

            if (producer.Type == StructureType.LivingHeartCore)
            {
                ResourceManager rm = ResourceManager.InstanceIfExists;
                if (rm == null || !rm.TrySpend(res, amount)) return false;
                return true;
            }

            return TryConsumeNodeResource(producer.GridPos, res, amount);
        }

        private bool ConsumerNeedsResource(IGridEntity consumer, ResourceType res)
        {
            if (!GetRequiredSupplyResource(consumer, out ResourceType needed) || needed != res)
            {
                return false;
            }
            return GetNodeBufferedAmount(consumer.GridPos, res) < NodeBufferCapacity;
        }

        private static bool GetRequiredSupplyResource(IGridEntity consumer, out ResourceType resource)
        {
            resource = ResourceType.Biomass;
            if (consumer == null) return false;

            StructureDefinition def = BalanceDatabase.GetStructure(consumer.Type);
            if (def == null) return false;

            switch (consumer.Type)
            {
                case StructureType.FlytrapTurret:
                case StructureType.MendTendril:
                case StructureType.BioReactor:
                    resource = ResourceType.Biomass;
                    return true;
                case StructureType.SporeFlowerTurret:
                    resource = ResourceType.Spores;
                    return true;
                case StructureType.BileCannonTurret:
                case StructureType.MutagenIncubator:
                    resource = ResourceType.BioPlasma;
                    return true;
                default:
                    if (def.ammoPerShot > 0f)
                    {
                        resource = def.powerOrAmmoResource;
                        return true;
                    }
                    return false;
            }
        }

        private static float ComputeConsumerPriorityWeight(IGridEntity consumer, NetworkPriorityMode mode)
        {
            if (consumer is IResourceConsumer custom)
            {
                return Mathf.Max(0.1f, custom.GetResourceNeedWeight(mode));
            }

            StructureDefinition def = BalanceDatabase.GetStructure(consumer.Type);
            StructureCategory cat = def != null ? def.category : StructureCategory.Support;

            switch (mode)
            {
                case NetworkPriorityMode.PrioritizeTurrets:
                    if (cat == StructureCategory.Turret || consumer.Type == StructureType.MendTendril) return 4.0f;
                    return 0.6f;

                case NetworkPriorityMode.PrioritizeReactors:
                    if (cat == StructureCategory.Production) return 4.0f;
                    return 0.6f;

                case NetworkPriorityMode.PrioritizeCore:
                    if (consumer.Type == StructureType.LivingHeartCore) return 4.5f;
                    return 0.5f;

                default: // Balanced
                    return 1.0f;
            }
        }

        private void SpawnPacketAlongCluster(
            ConnectedCluster cluster,
            IGridEntity producer,
            IGridEntity consumer,
            ResourceType resource,
            float amount)
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return;

            PacketVisual packet = AcquirePacket();
            if (packet == null)
            {
                // Deliver immediately if visual cap reached
                DeliverPacketPayload(consumer, consumer.GridPos, resource, amount);
                return;
            }

            BuildVeinPathWaypoints(grid, cluster, producer, consumer.GridPos, GetFootprintSize(consumer.Type), packet.Waypoints);
            if (packet.Waypoints.Count < 2)
            {
                ReleasePacket(packet);
                DeliverPacketPayload(consumer, consumer.GridPos, resource, amount);
                return;
            }

            ConfigureAndLaunchPacket(packet, resource, amount, consumer, consumer.GridPos, cluster.AverageSpeed);
        }

        private void SpawnVisualPulseToCell(ConnectedCluster cluster, IGridEntity producer, Vector2Int destConduitCell, ResourceType res)
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return;

            PacketVisual packet = AcquirePacket();
            if (packet == null) return;

            BuildVeinPathWaypoints(grid, cluster, producer, destConduitCell, Vector2Int.one, packet.Waypoints);
            if (packet.Waypoints.Count < 2)
            {
                ReleasePacket(packet);
                return;
            }

            ConfigureAndLaunchPacket(packet, res, 0f, null, destConduitCell, cluster.AverageSpeed * 0.85f);
        }

        private void ConfigureAndLaunchPacket(
            PacketVisual packet,
            ResourceType resource,
            float amount,
            IGridEntity targetEntity,
            Vector2Int targetAnchor,
            float speed)
        {
            packet.Resource = resource;
            packet.Amount = amount;
            packet.TargetEntity = targetEntity;
            packet.TargetNodeAnchor = targetAnchor;
            packet.CurrentSegmentIndex = 0;
            packet.SegmentProgress = 0f;
            packet.SpeedTilesPerSec = Mathf.Clamp(speed, 2.0f, 8.0f);
            packet.Active = true;

            packet.Renderer.sprite = SpriteRegistry.GetPacketSprite(resource);
            Color col = BalanceDatabase.GetResourceColor(resource);
            packet.Renderer.color = new Color(col.r, col.g, col.b, 0.95f);

            packet.Transform.position = packet.Waypoints[0];
            packet.Transform.localScale = Vector3.one * (amount > 0f ? 0.34f : 0.24f);
            packet.GameObject.SetActive(true);

            _activePackets.Add(packet);
        }

        private void BuildVeinPathWaypoints(
            BioGridManager grid,
            ConnectedCluster cluster,
            IGridEntity producer,
            Vector2Int destAnchor,
            Vector2Int destFootprint,
            List<Vector3> outWaypoints)
        {
            outWaypoints.Clear();
            _bfsQueue.Clear();
            _bfsParent.Clear();

            Vector2Int prodAnchor = producer.GridPos;
            Vector2Int prodFootprint = GetFootprintSize(producer.Type);

            // Seed all cluster conduit cells adjacent to producer footprint
            for (int dx = 0; dx < prodFootprint.x; dx++)
            {
                for (int dy = 0; dy < prodFootprint.y; dy++)
                {
                    Vector2Int pCell = new Vector2Int(prodAnchor.x + dx, prodAnchor.y + dy);
                    for (int d = 0; d < CardinalDirs.Length; d++)
                    {
                        Vector2Int adj = pCell + CardinalDirs[d];
                        if (cluster.ConduitCells.Contains(adj) && !_bfsParent.ContainsKey(adj))
                        {
                            _bfsParent[adj] = adj; // Root sentinel
                            _bfsQueue.Enqueue(adj);
                        }
                    }
                }
            }

            Vector2Int foundEndConduit = new Vector2Int(-999, -999);
            while (_bfsQueue.Count > 0)
            {
                Vector2Int cur = _bfsQueue.Dequeue();
                if (cur == destAnchor || IsAdjacentToFootprint(cur, destAnchor, destFootprint))
                {
                    foundEndConduit = cur;
                    break;
                }

                for (int d = 0; d < CardinalDirs.Length; d++)
                {
                    Vector2Int nb = cur + CardinalDirs[d];
                    if (cluster.ConduitCells.Contains(nb) && !_bfsParent.ContainsKey(nb))
                    {
                        _bfsParent[nb] = cur;
                        _bfsQueue.Enqueue(nb);
                    }
                }
            }

            Vector3 startWorld = grid.GetFootprintWorldCenter(prodAnchor, prodFootprint);
            Vector3 endWorld = grid.GetFootprintWorldCenter(destAnchor, destFootprint);

            if (foundEndConduit.x == -999)
            {
                outWaypoints.Add(startWorld);
                outWaypoints.Add(endWorld);
                return;
            }

            // Reconstruct conduit chain
            List<Vector2Int> chain = new List<Vector2Int>(16);
            Vector2Int trace = foundEndConduit;
            int safety = 256;
            while (safety-- > 0)
            {
                chain.Add(trace);
                if (!_bfsParent.TryGetValue(trace, out Vector2Int parent) || parent == trace)
                {
                    break;
                }
                trace = parent;
            }
            chain.Reverse();

            outWaypoints.Add(startWorld);
            for (int i = 0; i < chain.Count; i++)
            {
                outWaypoints.Add(grid.GridToWorldCenter(chain[i]));
            }
            if (Vector3.SqrMagnitude(outWaypoints[outWaypoints.Count - 1] - endWorld) > 0.05f)
            {
                outWaypoints.Add(endWorld);
            }
        }

        private static bool IsAdjacentToFootprint(Vector2Int cell, Vector2Int anchor, Vector2Int footprint)
        {
            int minX = anchor.x;
            int maxX = anchor.x + footprint.x - 1;
            int minY = anchor.y;
            int maxY = anchor.y + footprint.y - 1;

            int dx = cell.x < minX ? (minX - cell.x) : (cell.x > maxX ? (cell.x - maxX) : 0);
            int dy = cell.y < minY ? (minY - cell.y) : (cell.y > maxY ? (cell.y - maxY) : 0);
            return (dx + dy) == 1;
        }

        private void TickActivePackets(float simDt)
        {
            for (int i = _activePackets.Count - 1; i >= 0; i--)
            {
                PacketVisual p = _activePackets[i];
                if (!p.Active || p.Waypoints.Count < 2)
                {
                    ReleasePacket(p);
                    _activePackets.RemoveAt(i);
                    continue;
                }

                float remainingMove = p.SpeedTilesPerSec * simDt;
                bool arrived = false;

                while (remainingMove > 0f && p.CurrentSegmentIndex < p.Waypoints.Count - 1)
                {
                    Vector3 a = p.Waypoints[p.CurrentSegmentIndex];
                    Vector3 b = p.Waypoints[p.CurrentSegmentIndex + 1];
                    float segLen = Mathf.Max(0.05f, Vector3.Distance(a, b));
                    float distLeftOnSeg = (1f - p.SegmentProgress) * segLen;

                    if (remainingMove >= distLeftOnSeg)
                    {
                        remainingMove -= distLeftOnSeg;
                        p.CurrentSegmentIndex++;
                        p.SegmentProgress = 0f;
                        if (p.CurrentSegmentIndex >= p.Waypoints.Count - 1)
                        {
                            arrived = true;
                            break;
                        }
                    }
                    else
                    {
                        p.SegmentProgress += remainingMove / segLen;
                        remainingMove = 0f;
                    }
                }

                if (arrived)
                {
                    if (p.Amount > 0f)
                    {
                        DeliverPacketPayload(p.TargetEntity, p.TargetNodeAnchor, p.Resource, p.Amount);
                    }
                    ReleasePacket(p);
                    _activePackets.RemoveAt(i);
                }
                else
                {
                    Vector3 a = p.Waypoints[p.CurrentSegmentIndex];
                    Vector3 b = p.Waypoints[p.CurrentSegmentIndex + 1];
                    p.Transform.position = Vector3.Lerp(a, b, p.SegmentProgress);

                    float pulse = 1f + Mathf.Sin(Time.time * 10f + i) * 0.14f;
                    float baseScale = p.Amount > 0f ? 0.34f : 0.24f;
                    p.Transform.localScale = Vector3.one * (baseScale * pulse);
                }
            }
        }

        private void DeliverPacketPayload(IGridEntity target, Vector2Int targetAnchor, ResourceType resource, float amount)
        {
            if (amount <= 0f) return;

            if (target != null && target.Type == StructureType.LivingHeartCore)
            {
                ResourceManager.Instance?.Add(resource, amount);
                return;
            }

            if (target is IResourceConsumer consumerInterface)
            {
                float accepted = consumerInterface.ReceiveResource(resource, amount);
                float leftover = amount - accepted;
                if (leftover > 0.01f)
                {
                    AddToNodeBuffer(targetAnchor, resource, leftover);
                }
                return;
            }

            AddToNodeBuffer(targetAnchor, resource, amount);
        }

        // --- Public Node Buffer & Network Helper API for Wave 2 Systems ---

        public bool IsNodeConnected(Vector2Int structureAnchor)
        {
            if (_topologyDirty) RebuildNow();
            return _connectedNodeAnchors.Contains(structureAnchor);
        }

        public float GetNodeBufferedAmount(Vector2Int structureAnchor, ResourceType resource)
        {
            return _nodeBuffers.TryGetValue((structureAnchor, resource), out float val) ? val : 0f;
        }

        public void PushProducedResource(Vector2Int producerAnchor, ResourceType resource, float amount)
        {
            if (amount <= 0f) return;

            // If connected to veins, stage in node buffer so VeinNetworkSystem visibly pumps glowing packets!
            // If standalone (not connected to veins), credit ResourceManager directly so player still gets yield!
            if (IsNodeConnected(producerAnchor))
            {
                AddToNodeBuffer(producerAnchor, resource, amount);
            }
            else
            {
                ResourceManager.Instance?.Add(resource, amount);
            }
        }

        public bool TryConsumeNodeResource(Vector2Int structureAnchor, ResourceType resource, float amount)
        {
            if (amount <= 0f) return true;
            var key = (structureAnchor, resource);
            if (!_nodeBuffers.TryGetValue(key, out float current) || current + 0.001f < amount)
            {
                return false;
            }
            _nodeBuffers[key] = Mathf.Max(0f, current - amount);
            return true;
        }

        /// <summary>
        /// Convenience fire-rate / efficiency multiplier for turrets:
        /// 1.25x when actively supplied via VeinNetwork buffer, 0.70x fallback if dry/unconnected!
        /// </summary>
        public float ConsumeTurretShotAndGetMultiplier(Vector2Int turretAnchor, ResourceType ammoResource, float ammoPerShot)
        {
            if (ammoPerShot <= 0f) return 1.0f;
            if (TryConsumeNodeResource(turretAnchor, ammoResource, ammoPerShot))
            {
                return 1.25f;
            }
            return 0.70f;
        }

        private void AddToNodeBuffer(Vector2Int anchor, ResourceType resource, float delta)
        {
            var key = (anchor, resource);
            float cur = _nodeBuffers.TryGetValue(key, out float existing) ? existing : 0f;
            _nodeBuffers[key] = Mathf.Clamp(cur + delta, 0f, NodeBufferCapacity);
        }

        // --- Packet Pool Helpers ---

        private void PrewarmPacketPool(int count)
        {
            for (int i = 0; i < count; i++)
            {
                PacketVisual p = CreatePacketInstance();
                p.GameObject.SetActive(false);
                _packetPool.Enqueue(p);
            }
        }

        private PacketVisual AcquirePacket()
        {
            if (_packetPool.Count > 0)
            {
                return _packetPool.Dequeue();
            }
            if (_activePackets.Count >= MaxActivePackets)
            {
                return null;
            }
            return CreatePacketInstance();
        }

        private void ReleasePacket(PacketVisual packet)
        {
            if (packet == null) return;
            packet.Active = false;
            packet.Waypoints.Clear();
            packet.TargetEntity = null;
            if (packet.GameObject != null)
            {
                packet.GameObject.SetActive(false);
            }
            _packetPool.Enqueue(packet);
        }

        private PacketVisual CreatePacketInstance()
        {
            GameObject go = new GameObject("VeinPulse");
            go.transform.SetParent(_packetsRoot, false);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 6;
            sr.sprite = SpriteRegistry.GetSoftCircleSprite();

            return new PacketVisual
            {
                GameObject = go,
                Transform = go.transform,
                Renderer = sr,
                Active = false
            };
        }

        // --- Static Classification Helpers ---

        public static bool IsConduitType(StructureType type)
        {
            return type == StructureType.VeinBiomass ||
                   type == StructureType.VeinPlasma ||
                   type == StructureType.VeinArtery;
        }

        public static int GetConduitTier(StructureType type)
        {
            switch (type)
            {
                case StructureType.VeinArtery: return 3;
                case StructureType.VeinPlasma: return 2;
                case StructureType.VeinBiomass: return 1;
                default: return 0;
            }
        }

        public static float GetConduitPacketSpeed(int tier)
        {
            switch (tier)
            {
                case 3: return 6.5f;
                case 2: return 4.4f;
                default: return 2.9f;
            }
        }

        private static Vector2Int GetFootprintSize(StructureType type)
        {
            StructureDefinition def = BalanceDatabase.GetStructure(type);
            return def != null ? def.footprintSize : Vector2Int.one;
        }

        private static bool ActsAsProducer(IGridEntity entity)
        {
            if (entity is IResourceProducer) return true;
            switch (entity.Type)
            {
                case StructureType.LivingHeartCore:
                case StructureType.PulseBulbGenerator:
                case StructureType.SymbioticHarvester:
                case StructureType.BioReactor:
                case StructureType.MutagenIncubator:
                    return true;
                default:
                    return false;
            }
        }

        private static bool ActsAsConsumer(IGridEntity entity)
        {
            if (entity is IResourceConsumer) return true;
            switch (entity.Type)
            {
                case StructureType.LivingHeartCore:
                case StructureType.BioReactor:
                case StructureType.FlytrapTurret:
                case StructureType.SporeFlowerTurret:
                case StructureType.BileCannonTurret:
                case StructureType.MendTendril:
                case StructureType.MutagenIncubator:
                    return true;
                default:
                    return false;
            }
        }
    }
}
