using System;
using System.Collections.Generic;
using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Audio;

namespace BioFarme.Grid
{
    /// <summary>
    /// Built-in grid structure component used for starter colony layout (Heart Core, starter BoneWalls, starter Veins)
    /// and usable as a fallback structure node. Automatically replaced if a specialized Wave 2 component registers at the same cell.
    /// </summary>
    public sealed class BuiltInStructureNode : MonoBehaviour, IGridEntity, IDamageable, IInteractable
    {
        public Vector2Int GridPos { get; private set; }
        public StructureType Type { get; private set; }
        public Vector2Int Footprint { get; private set; } = Vector2Int.one;
        public bool IsStarterPlaceholder { get; private set; }

        public float CurrentHp { get; private set; }
        public float MaxHp { get; private set; }
        public Vector2 WorldPosition => transform.position;
        public bool IsAlive => CurrentHp > 0f;

        private SpriteRenderer _renderer;
        private float _pulsePhase;

        public void Initialize(Vector2Int anchorGridPos, StructureType type, SpriteRenderer sr, bool isStarter)
        {
            GridPos = anchorGridPos;
            Type = type;
            _renderer = sr;
            IsStarterPlaceholder = isStarter;

            StructureDefinition def = BalanceDatabase.GetStructure(type);
            Footprint = def != null ? def.footprintSize : Vector2Int.one;
            MaxHp = def != null && def.maxHp > 0f ? def.maxHp : 150f;
            CurrentHp = MaxHp;

            if (type == StructureType.LivingHeartCore)
            {
                GameEvents.RaiseCoreHealthChanged(CurrentHp, MaxHp);
            }
        }

        private void Update()
        {
            if (Type == StructureType.LivingHeartCore && IsAlive)
            {
                float dt = GameManager.InstanceIfExists != null ? GameManager.InstanceIfExists.SimulationDeltaTime : Time.deltaTime;
                _pulsePhase += dt * 3.2f;
                float pulse = 1f + Mathf.Sin(_pulsePhase) * 0.035f;
                transform.localScale = new Vector3(pulse, pulse, 1f);
            }
        }

        public void RefreshAutoTileSprite(int mask)
        {
            if (_renderer == null) return;
            StructureDefinition def = BalanceDatabase.GetStructure(Type);
            if (def != null && def.isConduit)
            {
                _renderer.sprite = SpriteRegistry.GetVeinAutoTileSprite(Type, mask);
            }
            else if (Type == StructureType.BoneWall || Type == StructureType.OrganicFence)
            {
                _renderer.sprite = SpriteRegistry.GetWallAutoTileSprite(Type, mask);
            }
        }

        public void TakeDamage(float amount, Vector2 hitSource)
        {
            if (!IsAlive || amount <= 0f) return;

            CurrentHp = Mathf.Max(0f, CurrentHp - amount);
            WorldPopupManager.SpawnDamage(WorldPosition, amount, false);

            if (Type == StructureType.LivingHeartCore)
            {
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(AudioKeys.SfxCoreHit, 0.9f, 0.04f);
                GameEvents.RaiseCoreHealthChanged(CurrentHp, MaxHp);
            }
            else
            {
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxEnemyHit, WorldPosition, 0.6f, 0.08f);
            }

            if (CurrentHp <= 0f)
            {
                if (Type == StructureType.LivingHeartCore)
                {
                    GameManager.Instance?.TriggerDefeat("Живое Сердце Колонии было разорвано роем!");
                }
                else
                {
                    if (BioGridManager.InstanceIfExists != null)
                    {
                        BioGridManager.InstanceIfExists.UnregisterStructure(GridPos);
                    }
                    Destroy(gameObject);
                }
            }
        }

        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f || CurrentHp >= MaxHp) return;
            float before = CurrentHp;
            CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
            float healed = CurrentHp - before;
            if (healed >= 1f)
            {
                WorldPopupManager.SpawnHeal(WorldPosition, healed);
            }
            if (Type == StructureType.LivingHeartCore)
            {
                GameEvents.RaiseCoreHealthChanged(CurrentHp, MaxHp);
            }
        }

        public string GetInteractionPrompt()
        {
            if (Type == StructureType.LivingHeartCore)
            {
                if (GameManager.InstanceIfExists != null && GameManager.InstanceIfExists.HeartAwakeningPercent >= 99.9f)
                {
                    return "[E] Пробудить Живое Сердце Колонии!";
                }
                return "[E] Дефибрилляция Сердца (Мини-игра)";
            }
            if (Type == StructureType.MutagenIncubator)
            {
                return "[E] Гено-сплайсинг мутагена (Мини-игра)";
            }
            return string.Empty;
        }

        public bool CanInteract()
        {
            return IsAlive && (Type == StructureType.LivingHeartCore || Type == StructureType.MutagenIncubator);
        }

        public void Interact()
        {
            if (!CanInteract() || GameManager.Instance == null) return;

            if (Type == StructureType.LivingHeartCore)
            {
                if (GameManager.Instance.HeartAwakeningPercent >= 99.9f &&
                    GameManager.Instance.WavesSurvived >= BalanceDatabase.TotalWaves)
                {
                    GameManager.Instance.TriggerVictory();
                    return;
                }
                GameManager.Instance.OpenMiniGame(MiniGameKind.CoreDefibrillation);
            }
            else if (Type == StructureType.MutagenIncubator)
            {
                GameManager.Instance.OpenMiniGame(MiniGameKind.GeneSplicing);
            }
        }
    }

    /// <summary>
    /// Authoritative 36x24 subterranean cavern grid manager (CellSize = 1.0f = 128x128 px).
    /// Handles ground tiles, soil tilling/hydration, structure occupancy, auto-tile bitmasks, and 4 Hive Burrows.
    /// </summary>
    public sealed class BioGridManager : MonoBehaviour
    {
        public const int Width = 36;
        public const int Height = 24;
        public const float CellSize = 1.0f;

        // 3x3 Living Heart Core occupies (17..19, 11..13)
        public static readonly Vector2Int HeartAnchor = new Vector2Int(17, 11);
        public static readonly Vector2Int HeartCenterCell = new Vector2Int(18, 12);

        private struct CellState
        {
            public TileGroundType groundType;
            public bool inherentlyFertile;
            public float hydration; // 0..1
            public bool hasCrop;
            public ICropEntity cropEntity;
            public IGridEntity structureEntity;
            public Vector2Int structureAnchor;
            public SpriteRenderer groundRenderer;
        }

        private static BioGridManager _instance;
        private static bool _applicationQuitting;

        public static BioGridManager InstanceIfExists => _instance;

        public static BioGridManager Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<BioGridManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[BioGridManager]");
                        _instance = go.AddComponent<BioGridManager>();
                    }
                }
                return _instance;
            }
        }

        private readonly CellState[,] _cells = new CellState[Width, Height];
        private readonly Vector2Int[] _burrowCells =
        {
            new Vector2Int(1, 12),  // 0: West Burrow
            new Vector2Int(34, 12), // 1: East Burrow
            new Vector2Int(18, 22), // 2: North Burrow
            new Vector2Int(18, 1)   // 3: South Burrow
        };

        private Transform _groundVisualsRoot;
        private Transform _starterStructuresRoot;
        private FlowFieldPathfinder _pathfinder;
        private VeinNetworkSystem _veinNetwork;
        private bool _mapGenerated;
        private int _lastManualHydrationFrame = -1;

        public Vector2Int HeartCoreAnchor => HeartAnchor;
        public Vector2Int HeartCoreCenterGrid => HeartCenterCell;
        public Vector3 HeartCoreCenterWorld => new Vector3(18.5f * CellSize, 12.5f * CellSize, 0f);
        public Rect WorldMapBounds => new Rect(0f, 0f, Width * CellSize, Height * CellSize);
        public int BurrowCount => _burrowCells.Length;
        public IReadOnlyList<Vector2Int> BurrowGridPositions => _burrowCells;
        public FlowFieldPathfinder Pathfinder => _pathfinder != null ? _pathfinder : FlowFieldPathfinder.Instance;
        public VeinNetworkSystem VeinNetwork => _veinNetwork != null ? _veinNetwork : VeinNetworkSystem.Instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            _pathfinder = GetComponent<FlowFieldPathfinder>();
            if (_pathfinder == null)
            {
                _pathfinder = gameObject.AddComponent<FlowFieldPathfinder>();
            }

            _veinNetwork = GetComponent<VeinNetworkSystem>();
            if (_veinNetwork == null)
            {
                _veinNetwork = gameObject.AddComponent<VeinNetworkSystem>();
            }

            EnsureMapGenerated();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }

        private void Update()
        {
            // Auto-tick hydration unless Farming system already called TickSoilHydration manually this frame
            if (_lastManualHydrationFrame == Time.frameCount) return;

            float simDt = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.SimulationDeltaTime
                : Time.deltaTime;

            if (simDt > 0f)
            {
                TickSoilHydrationInternal(simDt);
            }
        }

        public void EnsureMapGenerated()
        {
            if (_mapGenerated) return;
            RegenerateCavernMap();
        }

        /// <summary>
        /// Generates the 36x24 cavern layout matching the biopunk colony showcase:
        /// 3x3 central Living Heart Core, fertile moss sanctuaries, pre-tilled & irrigated starter beds,
        /// starter BoneWall choke barriers, starter Vein conduits, and 4 cardinal Hive Burrows.
        /// </summary>
        public void RegenerateCavernMap()
        {
            _mapGenerated = true;

            if (_groundVisualsRoot != null)
            {
                Destroy(_groundVisualsRoot.gameObject);
            }
            if (_starterStructuresRoot != null)
            {
                Destroy(_starterStructuresRoot.gameObject);
            }

            GameObject groundGo = new GameObject("GroundTiles");
            groundGo.transform.SetParent(transform, false);
            _groundVisualsRoot = groundGo.transform;

            GameObject structsGo = new GameObject("StarterStructures");
            structsGo.transform.SetParent(transform, false);
            _starterStructuresRoot = structsGo.transform;

            // Clear cell array
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    _cells[x, y] = default;
                }
            }

            Vector2 center = new Vector2(18f, 12f);

            // Step 1: Carve organic subterranean cavern, rock perimeter, and fertile moss zones
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    float normDx = (x - center.x) / 16.2f;
                    float normDy = (y - center.y) / 10.4f;
                    float radial = Mathf.Sqrt(normDx * normDx + normDy * normDy);

                    // Deterministic organic cave wall modulation
                    float angle = Mathf.Atan2(normDy, normDx);
                    float caveEdge = 0.92f + 0.07f * Mathf.Sin(angle * 5f + 0.8f) + 0.05f * Mathf.Cos(angle * 3f);

                    bool borderRock = x <= 1 || x >= Width - 2 || y <= 1 || y >= Height - 2 || radial > caveEdge;

                    TileGroundType ground;
                    bool fertile = false;
                    float hydration = 0f;

                    if (borderRock)
                    {
                        ground = TileGroundType.CavernRock;
                    }
                    else
                    {
                        float mossNoise = Mathf.Sin(x * 0.52f + 1.1f) * Mathf.Cos(y * 0.58f + 0.4f);
                        float coreDist = Vector2.Distance(new Vector2(x, y), new Vector2(18f, 12f));
                        if ((coreDist >= 3.2f && coreDist <= 8.2f && mossNoise > -0.15f) || mossNoise > 0.38f)
                        {
                            ground = TileGroundType.MossFertile;
                            fertile = true;
                        }
                        else
                        {
                            ground = TileGroundType.FleshSoil;
                        }
                    }

                    _cells[x, y].groundType = ground;
                    _cells[x, y].inherentlyFertile = fertile;
                    _cells[x, y].hydration = hydration;
                }
            }

            // Step 2: Carve the 4 cardinal Hive Burrow tunnels cleanly to the central cavern
            // West tunnel (x = 1..8, y = 11..13)
            CarveTunnelRect(1, 11, 8, 13);
            // East tunnel (x = 27..34, y = 11..13)
            CarveTunnelRect(27, 11, 34, 13);
            // North tunnel (x = 17..19, y = 16..22)
            CarveTunnelRect(17, 16, 19, 22);
            // South tunnel (x = 17..19, y = 1..7)
            CarveTunnelRect(17, 1, 19, 7);

            // Stamp the 4 HiveBurrow entrance pits
            for (int i = 0; i < _burrowCells.Length; i++)
            {
                Vector2Int b = _burrowCells[i];
                _cells[b.x, b.y].groundType = TileGroundType.HiveBurrow;
                _cells[b.x, b.y].inherentlyFertile = false;
            }

            // Step 3: Stamp 3x3 Living Heart Core footprint at (17..19, 11..13)
            for (int x = 17; x <= 19; x++)
            {
                for (int y = 11; y <= 13; y++)
                {
                    _cells[x, y].groundType = TileGroundType.CoreFootprint;
                    _cells[x, y].inherentlyFertile = false;
                }
            }

            // Step 4: Stamp starter cultivated garden beds around the sanctuary
            // Left irrigated garden (WateredBed: x=13..15, y=8..9)
            for (int x = 13; x <= 15; x++)
            {
                for (int y = 8; y <= 9; y++)
                {
                    _cells[x, y].groundType = TileGroundType.WateredBed;
                    _cells[x, y].inherentlyFertile = true;
                    _cells[x, y].hydration = 1.0f;
                }
            }

            // Right pre-tilled garden (TilledBed: x=20..22, y=8..9)
            for (int x = 20; x <= 22; x++)
            {
                for (int y = 8; y <= 9; y++)
                {
                    _cells[x, y].groundType = TileGroundType.TilledBed;
                    _cells[x, y].inherentlyFertile = true;
                    _cells[x, y].hydration = 0.25f;
                }
            }

            // Step 5: Build ground tile SpriteRenderers
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    GameObject tileGo = new GameObject($"Tile_{x}_{y}");
                    tileGo.transform.SetParent(_groundVisualsRoot, false);
                    tileGo.transform.position = GridToWorldCenter(x, y);

                    SpriteRenderer sr = tileGo.AddComponent<SpriteRenderer>();
                    sr.sortingOrder = -100;
                    sr.sprite = SpriteRegistry.GetGroundTileSprite(_cells[x, y].groundType, x * 7 + y * 13);
                    _cells[x, y].groundRenderer = sr;
                }
            }

            // Step 6: Spawn starter Living Heart Core, starter BoneWall flanks, and starter Vein conduits
            SpawnBuiltInStructure(HeartAnchor, StructureType.LivingHeartCore, isStarter: true);

            // Starter West & East BoneWall defensive shoulders (with open tactical gaps!)
            SpawnBuiltInStructure(new Vector2Int(10, 10), StructureType.BoneWall, isStarter: true);
            SpawnBuiltInStructure(new Vector2Int(10, 11), StructureType.BoneWall, isStarter: true);
            SpawnBuiltInStructure(new Vector2Int(10, 13), StructureType.BoneWall, isStarter: true);
            SpawnBuiltInStructure(new Vector2Int(10, 14), StructureType.BoneWall, isStarter: true);

            SpawnBuiltInStructure(new Vector2Int(25, 10), StructureType.BoneWall, isStarter: true);
            SpawnBuiltInStructure(new Vector2Int(25, 11), StructureType.BoneWall, isStarter: true);
            SpawnBuiltInStructure(new Vector2Int(25, 13), StructureType.BoneWall, isStarter: true);
            SpawnBuiltInStructure(new Vector2Int(25, 14), StructureType.BoneWall, isStarter: true);

            // Starter Vein network radiating from Heart Core (including VeinPlasma irrigating the left garden!)
            SpawnBuiltInStructure(new Vector2Int(16, 12), StructureType.VeinBiomass, isStarter: true);
            SpawnBuiltInStructure(new Vector2Int(15, 12), StructureType.VeinBiomass, isStarter: true);
            SpawnBuiltInStructure(new Vector2Int(15, 11), StructureType.VeinPlasma, isStarter: true);
            SpawnBuiltInStructure(new Vector2Int(15, 10), StructureType.VeinPlasma, isStarter: true);

            SpawnBuiltInStructure(new Vector2Int(20, 12), StructureType.VeinBiomass, isStarter: true);
            SpawnBuiltInStructure(new Vector2Int(21, 12), StructureType.VeinBiomass, isStarter: true);

            // Recompute FlowField and VeinNetwork immediately
            _pathfinder?.RecomputeNow();
            _veinNetwork?.RebuildNow();
        }

        private void CarveTunnelRect(int minX, int minY, int maxX, int maxY)
        {
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (!IsInBounds(x, y)) continue;
                    _cells[x, y].groundType = TileGroundType.FleshSoil;
                }
            }
        }

        /// <summary>
        /// Spawns a functional BuiltInStructureNode on the grid (used for starter structures or quick building).
        /// </summary>
        public BuiltInStructureNode SpawnBuiltInStructure(Vector2Int anchorPos, StructureType type, bool isStarter = false)
        {
            if (!CanPlaceStructure(anchorPos, type)) return null;

            StructureDefinition def = BalanceDatabase.GetStructure(type);
            Vector2Int footprint = def != null ? def.footprintSize : Vector2Int.one;

            GameObject go = new GameObject($"Struct_{type}_{anchorPos.x}_{anchorPos.y}");
            if (_starterStructuresRoot != null)
            {
                go.transform.SetParent(_starterStructuresRoot, false);
            }
            go.transform.position = GetFootprintWorldCenter(anchorPos, footprint);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = (def != null && def.isConduit) ? -50 : 10;
            sr.sprite = SpriteRegistry.GetStructureSprite(type);

            BuiltInStructureNode node = go.AddComponent<BuiltInStructureNode>();
            node.Initialize(anchorPos, type, sr, isStarter);

            RegisterStructure(anchorPos, node);
            return node;
        }

        // --- Coordinate Conversions & Bounds ---

        public Vector2Int WorldToGrid(Vector3 worldPos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPos.x / CellSize),
                Mathf.FloorToInt(worldPos.y / CellSize));
        }

        public Vector2Int WorldToGrid(Vector2 worldPos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPos.x / CellSize),
                Mathf.FloorToInt(worldPos.y / CellSize));
        }

        public Vector3 GridToWorldCenter(Vector2Int gridPos)
        {
            return new Vector3((gridPos.x + 0.5f) * CellSize, (gridPos.y + 0.5f) * CellSize, 0f);
        }

        public Vector3 GridToWorldCenter(int x, int y)
        {
            return new Vector3((x + 0.5f) * CellSize, (y + 0.5f) * CellSize, 0f);
        }

        public Vector3 GetFootprintWorldCenter(Vector2Int anchorGridPos, Vector2Int footprintSize)
        {
            return new Vector3(
                (anchorGridPos.x + footprintSize.x * 0.5f) * CellSize,
                (anchorGridPos.y + footprintSize.y * 0.5f) * CellSize,
                0f);
        }

        public bool IsInBounds(Vector2Int gridPos)
        {
            return gridPos.x >= 0 && gridPos.x < Width && gridPos.y >= 0 && gridPos.y < Height;
        }

        public bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public Vector3 GetBurrowSpawnWorldPosition(int burrowIndex)
        {
            if (_burrowCells.Length == 0) return Vector3.one;
            int idx = Mathf.Abs(burrowIndex) % _burrowCells.Length;
            return GridToWorldCenter(_burrowCells[idx]);
        }

        // --- Ground, Tilling, Irrigation & Crops ---

        public TileGroundType GetGroundType(Vector2Int gridPos)
        {
            EnsureMapGenerated();
            if (!IsInBounds(gridPos)) return TileGroundType.CavernRock;
            return _cells[gridPos.x, gridPos.y].groundType;
        }

        public void SetGroundType(Vector2Int gridPos, TileGroundType newType)
        {
            EnsureMapGenerated();
            if (!IsInBounds(gridPos)) return;

            ref CellState cell = ref _cells[gridPos.x, gridPos.y];
            if (cell.groundType == newType) return;

            cell.groundType = newType;
            if (newType == TileGroundType.MossFertile)
            {
                cell.inherentlyFertile = true;
            }
            if (cell.groundRenderer != null)
            {
                cell.groundRenderer.sprite = SpriteRegistry.GetGroundTileSprite(newType);
            }

            GameEvents.RaiseTileChanged(gridPos, newType);
        }

        public float GetHydration(Vector2Int gridPos)
        {
            if (!IsInBounds(gridPos)) return 0f;
            return _cells[gridPos.x, gridPos.y].hydration;
        }

        public bool IsTileWatered(Vector2Int gridPos)
        {
            if (!IsInBounds(gridPos)) return false;
            ref CellState c = ref _cells[gridPos.x, gridPos.y];
            return c.groundType == TileGroundType.WateredBed || c.hydration > 0.05f;
        }

        public bool IsTileFertile(Vector2Int gridPos)
        {
            if (!IsInBounds(gridPos)) return false;
            ref CellState c = ref _cells[gridPos.x, gridPos.y];
            return c.inherentlyFertile || c.groundType == TileGroundType.MossFertile;
        }

        public bool IsWalkable(Vector2Int gridPos)
        {
            EnsureMapGenerated();
            if (!IsInBounds(gridPos)) return false;
            ref CellState c = ref _cells[gridPos.x, gridPos.y];
            if (c.groundType == TileGroundType.CavernRock) return false;

            if (c.structureEntity != null)
            {
                StructureDefinition def = BalanceDatabase.GetStructure(c.structureEntity.Type);
                if (def != null && !def.isPassable) return false;
            }
            return true;
        }

        public bool CanTill(Vector2Int gridPos)
        {
            EnsureMapGenerated();
            if (!IsInBounds(gridPos)) return false;

            ref CellState cell = ref _cells[gridPos.x, gridPos.y];
            if (cell.groundType != TileGroundType.FleshSoil && cell.groundType != TileGroundType.MossFertile)
            {
                return false;
            }
            if (cell.structureEntity != null || cell.hasCrop)
            {
                return false;
            }
            return true;
        }

        public bool TillTile(Vector2Int gridPos)
        {
            if (!CanTill(gridPos)) return false;

            ref CellState cell = ref _cells[gridPos.x, gridPos.y];
            if (cell.groundType == TileGroundType.MossFertile)
            {
                cell.inherentlyFertile = true;
            }
            cell.hydration = 0.15f;
            SetGroundType(gridPos, TileGroundType.TilledBed);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxTill, GridToWorldCenter(gridPos), 0.75f, 0.08f);
            }
            return true;
        }

        public bool CanWater(Vector2Int gridPos)
        {
            EnsureMapGenerated();
            if (!IsInBounds(gridPos)) return false;
            TileGroundType g = _cells[gridPos.x, gridPos.y].groundType;
            return g == TileGroundType.TilledBed || g == TileGroundType.WateredBed;
        }

        public bool WaterTile(Vector2Int gridPos, float hydrationTarget = 1.0f)
        {
            if (!CanWater(gridPos)) return false;

            ref CellState cell = ref _cells[gridPos.x, gridPos.y];
            cell.hydration = Mathf.Clamp01(Mathf.Max(cell.hydration, hydrationTarget));
            if (cell.hydration > 0.05f && cell.groundType == TileGroundType.TilledBed)
            {
                SetGroundType(gridPos, TileGroundType.WateredBed);
            }
            return true;
        }

        public void TickSoilHydration(float deltaTime)
        {
            _lastManualHydrationFrame = Time.frameCount;
            TickSoilHydrationInternal(deltaTime);
        }

        private void TickSoilHydrationInternal(float deltaTime)
        {
            if (!_mapGenerated || deltaTime <= 0f) return;

            float weatherHydrationDelta = -0.018f;
            if (GameManager.InstanceIfExists != null && GameManager.InstanceIfExists.CurrentWeatherDefinition != null)
            {
                weatherHydrationDelta = GameManager.InstanceIfExists.CurrentWeatherDefinition.hydrationDeltaPerSec;
            }

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    ref CellState cell = ref _cells[x, y];
                    if (cell.groundType != TileGroundType.TilledBed && cell.groundType != TileGroundType.WateredBed)
                    {
                        continue;
                    }

                    cell.hydration = Mathf.Clamp01(cell.hydration + weatherHydrationDelta * deltaTime);

                    if (cell.hydration > 0.12f && cell.groundType == TileGroundType.TilledBed)
                    {
                        SetGroundType(new Vector2Int(x, y), TileGroundType.WateredBed);
                    }
                    else if (cell.hydration <= 0.01f && cell.groundType == TileGroundType.WateredBed)
                    {
                        SetGroundType(new Vector2Int(x, y), TileGroundType.TilledBed);
                    }
                }
            }
        }

        // --- Crop Occupancy Helpers ---

        public bool CanPlantCrop(Vector2Int gridPos)
        {
            EnsureMapGenerated();
            if (!IsInBounds(gridPos)) return false;
            ref CellState c = ref _cells[gridPos.x, gridPos.y];
            if (c.hasCrop) return false;
            if (c.structureEntity != null) return false;
            return c.groundType == TileGroundType.TilledBed || c.groundType == TileGroundType.WateredBed;
        }

        public bool HasCropAt(Vector2Int gridPos)
        {
            return IsInBounds(gridPos) && _cells[gridPos.x, gridPos.y].hasCrop;
        }

        public ICropEntity GetCropAt(Vector2Int gridPos)
        {
            return IsInBounds(gridPos) ? _cells[gridPos.x, gridPos.y].cropEntity : null;
        }

        public void SetCropOccupancy(Vector2Int gridPos, bool hasCrop, ICropEntity crop = null)
        {
            if (!IsInBounds(gridPos)) return;
            _cells[gridPos.x, gridPos.y].hasCrop = hasCrop;
            _cells[gridPos.x, gridPos.y].cropEntity = hasCrop ? crop : null;
        }

        // --- Structure Placement, Registration & Auto-Tile Bitmasks ---

        public bool CanPlaceStructure(Vector2Int anchorPos, StructureType type)
        {
            EnsureMapGenerated();
            if (type == StructureType.None) return false;

            StructureDefinition def = BalanceDatabase.GetStructure(type);
            Vector2Int footprint = def != null ? def.footprintSize : Vector2Int.one;

            for (int dx = 0; dx < footprint.x; dx++)
            {
                for (int dy = 0; dy < footprint.y; dy++)
                {
                    Vector2Int p = new Vector2Int(anchorPos.x + dx, anchorPos.y + dy);
                    if (!IsInBounds(p)) return false;

                    ref CellState cell = ref _cells[p.x, p.y];
                    if (cell.groundType == TileGroundType.CavernRock || cell.groundType == TileGroundType.HiveBurrow)
                    {
                        return false;
                    }

                    if (type == StructureType.LivingHeartCore)
                    {
                        if (cell.groundType != TileGroundType.CoreFootprint) return false;
                    }
                    else
                    {
                        if (cell.groundType == TileGroundType.CoreFootprint) return false;
                    }

                    if (cell.hasCrop) return false;

                    if (cell.structureEntity != null)
                    {
                        // Allow specialized Wave 2 LivingHeartCore to take over starter placeholder Core!
                        if (type == StructureType.LivingHeartCore &&
                            cell.structureEntity is BuiltInStructureNode starter &&
                            starter.IsStarterPlaceholder)
                        {
                            continue;
                        }
                        return false;
                    }
                }
            }

            return true;
        }

        public bool RegisterStructure(Vector2Int anchorPos, IGridEntity entity)
        {
            EnsureMapGenerated();
            if (entity == null || entity.Type == StructureType.None) return false;

            StructureDefinition def = BalanceDatabase.GetStructure(entity.Type);
            Vector2Int footprint = def != null ? def.footprintSize : Vector2Int.one;

            // Cleanly replace any starter placeholder occupying the target footprint
            HashSet<BuiltInStructureNode> startersToReplace = null;
            for (int dx = 0; dx < footprint.x; dx++)
            {
                for (int dy = 0; dy < footprint.y; dy++)
                {
                    Vector2Int p = new Vector2Int(anchorPos.x + dx, anchorPos.y + dy);
                    if (!IsInBounds(p)) return false;

                    IGridEntity existing = _cells[p.x, p.y].structureEntity;
                    if (existing != null && !ReferenceEquals(existing, entity))
                    {
                        if (existing is BuiltInStructureNode starter && starter.IsStarterPlaceholder)
                        {
                            if (startersToReplace == null) startersToReplace = new HashSet<BuiltInStructureNode>();
                            startersToReplace.Add(starter);
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
            }

            if (startersToReplace != null)
            {
                foreach (BuiltInStructureNode starter in startersToReplace)
                {
                    ClearFootprintInternal(starter.GridPos, starter);
                    if (starter != null && starter.gameObject != null)
                    {
                        Destroy(starter.gameObject);
                    }
                }
            }

            for (int dx = 0; dx < footprint.x; dx++)
            {
                for (int dy = 0; dy < footprint.y; dy++)
                {
                    Vector2Int p = new Vector2Int(anchorPos.x + dx, anchorPos.y + dy);
                    _cells[p.x, p.y].structureEntity = entity;
                    _cells[p.x, p.y].structureAnchor = anchorPos;
                }
            }

            RefreshAutoTilesAroundFootprint(anchorPos, footprint);

            if (def == null || def.blocksEnemies)
            {
                _pathfinder?.MarkDirty();
            }
            _veinNetwork?.MarkDirty();

            GameEvents.RaiseStructurePlaced(anchorPos, entity.Type, entity);
            return true;
        }

        public bool UnregisterStructure(Vector2Int anyCellInStructure)
        {
            EnsureMapGenerated();
            if (!IsInBounds(anyCellInStructure)) return false;

            IGridEntity entity = _cells[anyCellInStructure.x, anyCellInStructure.y].structureEntity;
            if (entity == null) return false;

            Vector2Int anchor = _cells[anyCellInStructure.x, anyCellInStructure.y].structureAnchor;
            StructureType removedType = entity.Type;
            StructureDefinition def = BalanceDatabase.GetStructure(removedType);
            Vector2Int footprint = def != null ? def.footprintSize : Vector2Int.one;

            ClearFootprintInternal(anchor, entity);
            RefreshAutoTilesAroundFootprint(anchor, footprint);

            if (def == null || def.blocksEnemies)
            {
                _pathfinder?.MarkDirty();
            }
            _veinNetwork?.MarkDirty();

            GameEvents.RaiseStructureRemoved(anchor, removedType);
            return true;
        }

        public bool UnregisterStructure(IGridEntity entity)
        {
            if (entity == null) return false;
            return UnregisterStructure(entity.GridPos);
        }

        private void ClearFootprintInternal(Vector2Int anchor, IGridEntity target)
        {
            StructureDefinition def = BalanceDatabase.GetStructure(target.Type);
            Vector2Int footprint = def != null ? def.footprintSize : Vector2Int.one;

            for (int dx = 0; dx < footprint.x; dx++)
            {
                for (int dy = 0; dy < footprint.y; dy++)
                {
                    Vector2Int p = new Vector2Int(anchor.x + dx, anchor.y + dy);
                    if (IsInBounds(p) && ReferenceEquals(_cells[p.x, p.y].structureEntity, target))
                    {
                        _cells[p.x, p.y].structureEntity = null;
                        _cells[p.x, p.y].structureAnchor = Vector2Int.zero;
                    }
                }
            }
        }

        public IGridEntity GetStructureAt(Vector2Int gridPos)
        {
            EnsureMapGenerated();
            if (!IsInBounds(gridPos)) return null;
            return _cells[gridPos.x, gridPos.y].structureEntity;
        }

        public StructureType GetStructureTypeAt(Vector2Int gridPos)
        {
            IGridEntity e = GetStructureAt(gridPos);
            return e != null ? e.Type : StructureType.None;
        }

        public IDamageable GetDamageableAt(Vector2Int gridPos)
        {
            return GetStructureAt(gridPos) as IDamageable;
        }

        /// <summary>
        /// Computes 4-bit cardinal mask (N=1, E=2, S=4, W=8) for BoneWall / OrganicFence auto-tiling.
        /// Connects seamlessly to adjacent walls, fences, and CavernRock borders!
        /// </summary>
        public int GetWallNeighborMask(Vector2Int pos)
        {
            int mask = 0;
            if (ConnectsToWall(pos + Vector2Int.up)) mask |= (int)CardinalNeighborMask.North;
            if (ConnectsToWall(pos + Vector2Int.right)) mask |= (int)CardinalNeighborMask.East;
            if (ConnectsToWall(pos + Vector2Int.down)) mask |= (int)CardinalNeighborMask.South;
            if (ConnectsToWall(pos + Vector2Int.left)) mask |= (int)CardinalNeighborMask.West;
            return mask;
        }

        private bool ConnectsToWall(Vector2Int p)
        {
            if (!IsInBounds(p)) return true;
            if (_cells[p.x, p.y].groundType == TileGroundType.CavernRock) return true;
            StructureType st = GetStructureTypeAt(p);
            return st == StructureType.BoneWall || st == StructureType.OrganicFence;
        }

        /// <summary>
        /// Computes 4-bit cardinal mask (N=1, E=2, S=4, W=8) for VeinBiomass / VeinPlasma / VeinArtery auto-tiling.
        /// Connects to adjacent conduits as well as LivingHeartCore, turrets, generators, reactors, harvesters, and tendrils!
        /// </summary>
        public int GetVeinNeighborMask(Vector2Int pos)
        {
            int mask = 0;
            if (ConnectsToVein(pos + Vector2Int.up)) mask |= (int)CardinalNeighborMask.North;
            if (ConnectsToVein(pos + Vector2Int.right)) mask |= (int)CardinalNeighborMask.East;
            if (ConnectsToVein(pos + Vector2Int.down)) mask |= (int)CardinalNeighborMask.South;
            if (ConnectsToVein(pos + Vector2Int.left)) mask |= (int)CardinalNeighborMask.West;
            return mask;
        }

        public bool ConnectsToVein(Vector2Int p)
        {
            if (!IsInBounds(p)) return false;
            IGridEntity e = _cells[p.x, p.y].structureEntity;
            if (e == null) return false;

            if (e is IResourceProducer || e is IResourceConsumer) return true;

            StructureType t = e.Type;
            if (t == StructureType.None || t == StructureType.BoneWall || t == StructureType.OrganicFence)
            {
                return false;
            }
            return true;
        }

        private void RefreshAutoTilesAroundFootprint(Vector2Int anchor, Vector2Int footprint)
        {
            for (int x = anchor.x - 1; x <= anchor.x + footprint.x; x++)
            {
                for (int y = anchor.y - 1; y <= anchor.y + footprint.y; y++)
                {
                    Vector2Int p = new Vector2Int(x, y);
                    if (!IsInBounds(p)) continue;

                    IGridEntity ent = _cells[p.x, p.y].structureEntity;
                    if (ent == null) continue;

                    if (ent is BuiltInStructureNode builtIn)
                    {
                        StructureDefinition def = BalanceDatabase.GetStructure(ent.Type);
                        if (def != null && def.isConduit)
                        {
                            builtIn.RefreshAutoTileSprite(GetVeinNeighborMask(p));
                        }
                        else if (ent.Type == StructureType.BoneWall || ent.Type == StructureType.OrganicFence)
                        {
                            builtIn.RefreshAutoTileSprite(GetWallNeighborMask(p));
                        }
                    }
                    else if (ent is MonoBehaviour mb && mb != null)
                    {
                        // Also auto-update SpriteRenderer on external Vein/BoneWall MonoBehaviours for instant crispness!
                        StructureDefinition def = BalanceDatabase.GetStructure(ent.Type);
                        if (def != null && def.isConduit)
                        {
                            SpriteRenderer sr = mb.GetComponent<SpriteRenderer>() ?? mb.GetComponentInChildren<SpriteRenderer>();
                            if (sr != null) sr.sprite = SpriteRegistry.GetVeinAutoTileSprite(ent.Type, GetVeinNeighborMask(p));
                        }
                        else if (ent.Type == StructureType.BoneWall || ent.Type == StructureType.OrganicFence)
                        {
                            SpriteRenderer sr = mb.GetComponent<SpriteRenderer>() ?? mb.GetComponentInChildren<SpriteRenderer>();
                            if (sr != null) sr.sprite = SpriteRegistry.GetWallAutoTileSprite(ent.Type, GetWallNeighborMask(p));
                        }
                    }
                }
            }
        }
    }
}
