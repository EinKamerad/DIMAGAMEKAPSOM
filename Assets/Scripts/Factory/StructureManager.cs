using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;
using BioFarme.Audio;
using BioFarme.Combat;

namespace BioFarme.Factory
{
    /// <summary>
    /// Authoritative builder, demolisher, and starter-colony spawner for "BioFarme: Сердце Колонии".
    /// Wires GameEvents.BuildStructureHandler and GameEvents.DemolishStructureHandler,
    /// upgrades starter BioGridManager nodes into real entities, and populates the initial sanctuary!
    /// </summary>
    public sealed class StructureManager : MonoBehaviour
    {
        private static StructureManager _instance;
        private static bool _applicationQuitting;
        private static bool _sceneHookRegistered;

        public static StructureManager InstanceIfExists => _instance;

        public static StructureManager Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<StructureManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[StructureManager]");
                        _instance = go.AddComponent<StructureManager>();
                    }
                }
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrapDefenseAndFactorySystems()
        {
            if (_applicationQuitting) return;

            if (!_sceneHookRegistered)
            {
                _sceneHookRegistered = true;
                SceneManager.sceneLoaded += OnSceneLoadedBootstrap;
            }

            EnsureManagersPresentInScene();
        }

        private static void OnSceneLoadedBootstrap(Scene scene, LoadSceneMode mode)
        {
            if (_applicationQuitting) return;
            EnsureManagersPresentInScene();
        }

        private static void EnsureManagersPresentInScene()
        {
            if (_applicationQuitting) return;
            if (string.Equals(SceneManager.GetActiveScene().name, "MainMenu", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = Instance;
            _ = ProjectileManager.Instance;
            _ = WaveDirector.Instance;
        }

        private Transform _structuresRoot;
        private readonly List<StructureBase> _activeStructures = new List<StructureBase>(128);

        public IReadOnlyList<StructureBase> ActiveStructures => _activeStructures;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            EnsureStructuresRoot();

            GameEvents.BuildStructureHandler = TryBuildStructure;
            GameEvents.DemolishStructureHandler = TryDemolishStructure;
        }

        private void OnEnable()
        {
            GameEvents.BuildStructureHandler = TryBuildStructure;
            GameEvents.DemolishStructureHandler = TryDemolishStructure;
            GameEvents.OnNewGameSessionInitialized += HandleNewGameSessionInitialized;
            GameEvents.OnStructurePlaced += HandleAnyStructurePlaced;
            GameEvents.OnStructureRemoved += HandleAnyStructureRemoved;
        }

        private void Start()
        {
            EnsureStarterColonyPopulated();
        }

        private void OnDisable()
        {
            GameEvents.OnNewGameSessionInitialized -= HandleNewGameSessionInitialized;
            GameEvents.OnStructurePlaced -= HandleAnyStructurePlaced;
            GameEvents.OnStructureRemoved -= HandleAnyStructureRemoved;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                if (GameEvents.BuildStructureHandler == TryBuildStructure)
                {
                    GameEvents.BuildStructureHandler = null;
                }
                if (GameEvents.DemolishStructureHandler == TryDemolishStructure)
                {
                    GameEvents.DemolishStructureHandler = null;
                }
                _instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }

        private void EnsureStructuresRoot()
        {
            if (_structuresRoot != null) return;
            GameObject rootGo = new GameObject("ColonyStructures");
            rootGo.transform.SetParent(transform, false);
            _structuresRoot = rootGo.transform;
        }

        private void HandleNewGameSessionInitialized()
        {
            // BioGridManager.RegenerateCavernMap() just rebuilt the cavern & fresh starter placeholders;
            // wipe any prior session entities cleanly and populate the new starter colony!
            ClearManagedEntitiesOnly();
            ProjectileManager.InstanceIfExists?.ClearAllProjectiles();
            EnsureStarterColonyPopulated();
        }

        private void ClearManagedEntitiesOnly()
        {
            _activeStructures.Clear();

            if (CoreHeartEntity.InstanceIfExists != null)
            {
                Destroy(CoreHeartEntity.InstanceIfExists.gameObject);
            }

            if (_structuresRoot != null)
            {
                Destroy(_structuresRoot.gameObject);
                _structuresRoot = null;
            }

            EnsureStructuresRoot();
        }

        /// <summary>
        /// Upgrades any BuiltInStructureNode starter placeholders on BioGridManager into real
        /// CoreHeartEntity / BoneWallEntity / VeinConduitEntity objects and places starter colony organisms!
        /// </summary>
        public void EnsureStarterColonyPopulated()
        {
            BioGridManager grid = BioGridManager.Instance;
            if (grid == null) return;

            grid.EnsureMapGenerated();
            EnsureStructuresRoot();

            IGridEntity heartSlot = grid.GetStructureAt(grid.HeartCoreAnchor);
            if (heartSlot is CoreHeartEntity && CoreHeartEntity.InstanceIfExists != null)
            {
                // Already upgraded for the current cavern map
                return;
            }

            // Collect all BuiltInStructureNode placeholders placed by BioGridManager
            List<(Vector2Int pos, StructureType type)> starterNodes = new List<(Vector2Int pos, StructureType type)>(32);
            HashSet<BuiltInStructureNode> visitedBuiltIns = new HashSet<BuiltInStructureNode>();

            for (int x = 0; x < BioGridManager.Width; x++)
            {
                for (int y = 0; y < BioGridManager.Height; y++)
                {
                    IGridEntity ent = grid.GetStructureAt(new Vector2Int(x, y));
                    if (ent is BuiltInStructureNode builtIn && visitedBuiltIns.Add(builtIn))
                    {
                        starterNodes.Add((builtIn.GridPos, builtIn.Type));
                    }
                }
            }

            // 1. Spawn the 3x3 Living Heart Core (automatically replaces starter LivingHeartCore placeholder)
            CoreHeartEntity.SpawnOnGrid(grid, _structuresRoot);

            // 2. Upgrade all starter BoneWalls and Vein conduits to real entities
            for (int i = 0; i < starterNodes.Count; i++)
            {
                var item = starterNodes[i];
                if (item.type == StructureType.LivingHeartCore || item.type == StructureType.None)
                {
                    continue;
                }
                SpawnStructureInternal(grid, item.pos, item.type, isStarter: true);
            }

            // 3. Extend starter arterial veins to West & East defensive flanks so turrets & machines pump packets on second 1!
            TrySpawnStarterOrganism(grid, new Vector2Int(14, 12), StructureType.VeinBiomass);
            TrySpawnStarterOrganism(grid, new Vector2Int(13, 12), StructureType.VeinBiomass);
            TrySpawnStarterOrganism(grid, new Vector2Int(12, 12), StructureType.VeinBiomass);

            TrySpawnStarterOrganism(grid, new Vector2Int(22, 12), StructureType.VeinBiomass);
            TrySpawnStarterOrganism(grid, new Vector2Int(23, 12), StructureType.VeinBiomass);

            // 4. Starter Colony Organisms around the Core (preserving irrigated gardens 13..15,8..9 & 20..22,8..9!):
            // - MutagenIncubator next to VeinPlasma at (14, 11) (fallback (15, 13))
            if (!TrySpawnStarterOrganism(grid, new Vector2Int(14, 11), StructureType.MutagenIncubator))
            {
                TrySpawnStarterOrganism(grid, new Vector2Int(15, 13), StructureType.MutagenIncubator);
            }

            // - SymbioticHarvester at (14, 10) tending the irrigated garden bed (13..15, 8..9) & linked to VeinPlasma (15, 10)
            if (!TrySpawnStarterOrganism(grid, new Vector2Int(14, 10), StructureType.SymbioticHarvester))
            {
                TrySpawnStarterOrganism(grid, new Vector2Int(16, 9), StructureType.SymbioticHarvester);
            }

            // - PulseBulbGenerator at (21, 13) linked to East VeinBiomass (21, 12)
            if (!TrySpawnStarterOrganism(grid, new Vector2Int(21, 13), StructureType.PulseBulbGenerator))
            {
                TrySpawnStarterOrganism(grid, new Vector2Int(20, 13), StructureType.PulseBulbGenerator);
            }

            // - 2 FlytrapTurrets guarding West & East BoneWall choke flanks
            if (!TrySpawnStarterOrganism(grid, new Vector2Int(12, 13), StructureType.FlytrapTurret))
            {
                TrySpawnStarterOrganism(grid, new Vector2Int(11, 13), StructureType.FlytrapTurret);
            }

            if (!TrySpawnStarterOrganism(grid, new Vector2Int(23, 13), StructureType.FlytrapTurret))
            {
                TrySpawnStarterOrganism(grid, new Vector2Int(24, 13), StructureType.FlytrapTurret);
            }

            // 5. Refresh all auto-tiles and rebuild pathfinding + vein network topology
            RefreshAllManagedAutoTiles();
            FlowFieldPathfinder.Instance?.RecomputeNow();
            VeinNetworkSystem.Instance?.RebuildNow();
        }

        private bool TrySpawnStarterOrganism(BioGridManager grid, Vector2Int pos, StructureType type)
        {
            if (!grid.CanPlaceStructure(pos, type)) return false;
            return SpawnStructureInternal(grid, pos, type, isStarter: true) != null;
        }

        // --- Build & Demolish Handlers ---

        public bool TryBuildStructure(Vector2Int gridPos, StructureType type)
        {
            if (type == StructureType.None || type == StructureType.LivingHeartCore)
            {
                return false;
            }

            if (GameManager.InstanceIfExists != null && !GameManager.InstanceIfExists.IsGameplayInteractive)
            {
                return false;
            }

            BioGridManager grid = BioGridManager.Instance;
            ResourceManager rm = ResourceManager.Instance;
            if (grid == null || rm == null) return false;

            StructureDefinition def = BalanceDatabase.GetStructure(type);
            if (def == null) return false;

            // Support seamless Mindustry-style in-place Vein tier upgrade (e.g., VeinBiomass -> VeinPlasma -> VeinArtery)
            IGridEntity existing = grid.GetStructureAt(gridPos);
            bool isConduitUpgrade = false;
            if (existing != null && def.isConduit && VeinNetworkSystem.IsConduitType(existing.Type))
            {
                int oldTier = VeinNetworkSystem.GetConduitTier(existing.Type);
                int newTier = VeinNetworkSystem.GetConduitTier(type);
                if (newTier > oldTier)
                {
                    isConduitUpgrade = true;
                }
            }

            if (!isConduitUpgrade && !grid.CanPlaceStructure(gridPos, type))
            {
                return false;
            }

            if (!rm.TrySpend(def.costs))
            {
                return false;
            }

            if (isConduitUpgrade && existing != null)
            {
                StructureDefinition oldDef = BalanceDatabase.GetStructure(existing.Type);
                if (oldDef != null)
                {
                    rm.RefundPercentage(oldDef.costs, BalanceDatabase.DeconstructRefundFraction);
                }
                RemoveEntityImmediate(grid, existing);
            }

            StructureBase created = SpawnStructureInternal(grid, gridPos, type, isStarter: false);
            if (created == null)
            {
                // Refund 100% if placement unexpectedly failed
                rm.RefundPercentage(def.costs, 1.0f);
                return false;
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxBuild, created.WorldPosition, 0.78f, 0.06f);
            }

            GameManager.InstanceIfExists?.RecordStructureBuilt();
            return true;
        }

        public bool TryDemolishStructure(Vector2Int gridPos)
        {
            if (GameManager.InstanceIfExists != null && !GameManager.InstanceIfExists.IsGameplayInteractive)
            {
                return false;
            }

            BioGridManager grid = BioGridManager.Instance;
            if (grid == null || !grid.IsInBounds(gridPos)) return false;

            IGridEntity entity = grid.GetStructureAt(gridPos);
            if (entity == null || entity.Type == StructureType.LivingHeartCore)
            {
                return false;
            }

            StructureDefinition def = BalanceDatabase.GetStructure(entity.Type);
            if (def != null && !def.canDeconstruct)
            {
                return false;
            }

            Vector2 worldPos = grid.GridToWorldCenter(entity.GridPos);

            if (def != null && def.costs != null && def.costs.Length > 0 && ResourceManager.Instance != null)
            {
                ResourceManager.Instance.RefundPercentage(def.costs, BalanceDatabase.DeconstructRefundFraction);
                WorldPopupManager.SpawnText(
                    worldPos + Vector2.up * 0.3f,
                    "Возврат 70%",
                    new Color(0.86f, 0.94f, 0.78f, 1f),
                    0.90f,
                    false);
            }

            RemoveEntityImmediate(grid, entity);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxDemolish, worldPos, 0.75f, 0.06f);
            }

            return true;
        }

        private void RemoveEntityImmediate(BioGridManager grid, IGridEntity entity)
        {
            Vector2Int anchor = entity.GridPos;
            StructureDefinition def = BalanceDatabase.GetStructure(entity.Type);
            Vector2Int footprint = def != null ? def.footprintSize : Vector2Int.one;

            if (entity is StructureBase sb)
            {
                sb.MarkDemolishedCleanly();
                _activeStructures.Remove(sb);
            }

            grid.UnregisterStructure(anchor);
            RefreshAutoTilesInNeighborhood(grid, anchor, footprint);

            if (entity is MonoBehaviour mb && mb != null)
            {
                Destroy(mb.gameObject);
            }
        }

        private StructureBase SpawnStructureInternal(BioGridManager grid, Vector2Int anchorPos, StructureType type, bool isStarter)
        {
            EnsureStructuresRoot();
            StructureDefinition def = BalanceDatabase.GetStructure(type);
            Vector2Int footprint = def != null ? def.footprintSize : Vector2Int.one;

            GameObject go = new GameObject($"{type}_{anchorPos.x}_{anchorPos.y}");
            go.transform.SetParent(_structuresRoot, false);
            go.transform.position = grid.GetFootprintWorldCenter(anchorPos, footprint);

            StructureBase entity = AttachSpecializedComponent(go, type);
            entity.Initialize(anchorPos, type, def, isStarter);

            if (!grid.RegisterStructure(anchorPos, entity))
            {
                Destroy(go);
                return null;
            }

            _activeStructures.Add(entity);
            entity.RefreshNeighbors(propagateToCardinals: true);
            RefreshAutoTilesInNeighborhood(grid, anchorPos, footprint);
            return entity;
        }

        private static StructureBase AttachSpecializedComponent(GameObject go, StructureType type)
        {
            switch (type)
            {
                case StructureType.BoneWall:
                case StructureType.OrganicFence:
                    return go.AddComponent<BoneWallEntity>();

                case StructureType.VeinBiomass:
                case StructureType.VeinPlasma:
                case StructureType.VeinArtery:
                    return go.AddComponent<VeinConduitEntity>();

                case StructureType.FlytrapTurret:
                case StructureType.SporeFlowerTurret:
                case StructureType.BileCannonTurret:
                case StructureType.PulseBulbGenerator:
                    return go.AddComponent<BioTurretEntity>();

                case StructureType.SymbioticHarvester:
                case StructureType.BioReactor:
                case StructureType.MendTendril:
                case StructureType.MutagenIncubator:
                default:
                    return go.AddComponent<BioFactoryEntity>();
            }
        }

        public void NotifyStructureRemovedFromWorld(StructureBase structure, Vector2Int anchorPos)
        {
            if (structure != null)
            {
                _activeStructures.Remove(structure);
            }

            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid != null)
            {
                StructureDefinition def = structure != null ? structure.Definition : null;
                Vector2Int footprint = def != null ? def.footprintSize : Vector2Int.one;
                RefreshAutoTilesInNeighborhood(grid, anchorPos, footprint);
            }
        }

        private void HandleAnyStructurePlaced(Vector2Int pos, StructureType type, IGridEntity entity)
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return;
            StructureDefinition def = BalanceDatabase.GetStructure(type);
            Vector2Int footprint = def != null ? def.footprintSize : Vector2Int.one;
            RefreshAutoTilesInNeighborhood(grid, pos, footprint);
        }

        private void HandleAnyStructureRemoved(Vector2Int pos, StructureType type)
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return;
            StructureDefinition def = BalanceDatabase.GetStructure(type);
            Vector2Int footprint = def != null ? def.footprintSize : Vector2Int.one;
            RefreshAutoTilesInNeighborhood(grid, pos, footprint);
        }

        private static void RefreshAutoTilesInNeighborhood(BioGridManager grid, Vector2Int anchor, Vector2Int footprint)
        {
            if (grid == null) return;

            for (int x = anchor.x - 1; x <= anchor.x + footprint.x; x++)
            {
                for (int y = anchor.y - 1; y <= anchor.y + footprint.y; y++)
                {
                    Vector2Int p = new Vector2Int(x, y);
                    if (!grid.IsInBounds(p)) continue;

                    IGridEntity ent = grid.GetStructureAt(p);
                    if (ent is StructureBase sb)
                    {
                        sb.RefreshNeighbors(propagateToCardinals: false);
                    }
                }
            }
        }

        private void RefreshAllManagedAutoTiles()
        {
            for (int i = 0; i < _activeStructures.Count; i++)
            {
                if (_activeStructures[i] != null)
                {
                    _activeStructures[i].RefreshNeighbors(propagateToCardinals: false);
                }
            }
        }
    }
}
