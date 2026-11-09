using System.Collections.Generic;
using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;
using BioFarme.Audio;

namespace BioFarme.Farming
{
    /// <summary>
    /// Singleton farming manager handling seed planting (with smooth auto-tilling),
    /// starter sanctuary crop seeding, and active CropInstance tracking.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class CropSystem : MonoBehaviour
    {
        private static CropSystem _instance;
        private static bool _applicationQuitting;

        public static CropSystem InstanceIfExists => _instance;

        public static CropSystem Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<CropSystem>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[CropSystem]");
                        _instance = go.AddComponent<CropSystem>();
                    }
                }
                return _instance;
            }
        }

        private Transform _cropsRoot;
        private readonly List<CropInstance> _activeCrops = new List<CropInstance>(64);
        private bool _starterCropsSeeded;

        public IReadOnlyList<CropInstance> ActiveCrops => _activeCrops;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            EnsureCropsRoot();
            GameEvents.PlantCropHandler = TryPlantCrop;
        }

        private void OnEnable()
        {
            GameEvents.PlantCropHandler = TryPlantCrop;
            GameEvents.OnNewGameSessionInitialized += HandleNewGameSessionInitialized;
        }

        private void Start()
        {
            if (!_starterCropsSeeded && _activeCrops.Count == 0)
            {
                PlantStarterSanctuaryCrops();
            }
        }

        private void OnDisable()
        {
            GameEvents.OnNewGameSessionInitialized -= HandleNewGameSessionInitialized;
        }

        private void OnDestroy()
        {
            if (GameEvents.PlantCropHandler == TryPlantCrop)
            {
                GameEvents.PlantCropHandler = null;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }

        private void EnsureCropsRoot()
        {
            if (_cropsRoot != null) return;
            GameObject root = new GameObject("PlantedCrops");
            root.transform.SetParent(transform, false);
            _cropsRoot = root.transform;
        }

        private void HandleNewGameSessionInitialized()
        {
            ClearAllCrops();
            PlantStarterSanctuaryCrops();
        }

        public void ClearAllCrops()
        {
            for (int i = _activeCrops.Count - 1; i >= 0; i--)
            {
                CropInstance crop = _activeCrops[i];
                if (crop != null)
                {
                    WorldEntityRegistry.UnregisterCrop(crop.GridPos);
                    if (BioGridManager.InstanceIfExists != null)
                    {
                        BioGridManager.InstanceIfExists.SetCropOccupancy(crop.GridPos, false, null);
                    }
                    Destroy(crop.gameObject);
                }
            }
            _activeCrops.Clear();
            _starterCropsSeeded = false;
        }

        /// <summary>
        /// Plants 4 starter alien crops on the pre-tilled beds south-west and south-east of the Living Heart Core
        /// so the colony immediately feels alive at session start.
        /// </summary>
        public void PlantStarterSanctuaryCrops()
        {
            BioGridManager grid = BioGridManager.Instance;
            if (grid == null) return;

            grid.EnsureMapGenerated();
            _starterCropsSeeded = true;

            // Left irrigated bed (x=13..15, y=8..9) + Right pre-tilled bed (x=20..22, y=8..9)
            SpawnStarterCrop(new Vector2Int(14, 9), CropType.Sporecap, initialProgress01: 0.65f);
            SpawnStarterCrop(new Vector2Int(15, 9), CropType.CrimsonPod, initialProgress01: 0.45f);
            SpawnStarterCrop(new Vector2Int(14, 8), CropType.PlasmaBulb, initialProgress01: 0.30f);
            SpawnStarterCrop(new Vector2Int(21, 9), CropType.OssuaryRoot, initialProgress01: 0.25f);
        }

        private CropInstance SpawnStarterCrop(Vector2Int cell, CropType cropType, float initialProgress01)
        {
            BioGridManager grid = BioGridManager.Instance;
            if (grid == null || !grid.IsInBounds(cell)) return null;

            if (!grid.CanPlantCrop(cell))
            {
                if (grid.CanTill(cell))
                {
                    grid.TillTile(cell);
                }
                if (!grid.CanPlantCrop(cell)) return null;
            }

            CropDefinition def = BalanceDatabase.GetCrop(cropType);
            if (def == null) return null;

            CropInstance instance = CreateCropObject(cell, cropType, def, initialProgress01);
            GameEvents.RaiseCropPlanted(cell, cropType);
            return instance;
        }

        /// <summary>
        /// Attempts to plant a seed at the target cell. Automatically tills FleshSoil / MossFertile
        /// if needed and spends seed costs via ResourceManager.
        /// </summary>
        public bool TryPlantCrop(Vector2Int cell, CropType cropType)
        {
            if (cropType == CropType.None) return false;

            BioGridManager grid = BioGridManager.Instance;
            if (grid == null || !grid.IsInBounds(cell)) return false;

            if (grid.HasCropAt(cell) || WorldEntityRegistry.GetCropAt(cell) != null)
            {
                return false;
            }

            if (grid.GetStructureAt(cell) != null)
            {
                return false;
            }

            CropDefinition def = BalanceDatabase.GetCrop(cropType);
            if (def == null) return false;

            ResourceCost[] seedCosts = def.GetSeedCosts();

            // Verify affordability before auto-tilling so soil isn't tilled for free when out of spores/biomass
            if (ResourceManager.Instance != null && !ResourceManager.Instance.CanAfford(seedCosts))
            {
                ResourceManager.Instance.TrySpend(seedCosts); // Fires OnInsufficientResource for UI feedback
                return false;
            }

            if (!grid.CanPlantCrop(cell))
            {
                TileGroundType ground = grid.GetGroundType(cell);
                if ((ground == TileGroundType.FleshSoil || ground == TileGroundType.MossFertile) && grid.CanTill(cell))
                {
                    grid.TillTile(cell);
                }

                if (!grid.CanPlantCrop(cell))
                {
                    return false;
                }
            }

            if (ResourceManager.Instance != null && !ResourceManager.Instance.TrySpend(seedCosts))
            {
                return false;
            }

            CreateCropObject(cell, cropType, def, initialProgress01: 0f);

            Vector3 worldCenter = grid.GridToWorldCenter(cell);
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxPlant, worldCenter, 0.80f, 0.08f);
            }

            if (BioWeatherSystem.InstanceIfExists != null)
            {
                BioWeatherSystem.InstanceIfExists.SpawnPuffBurst(
                    worldCenter,
                    BalanceDatabase.GetResourceColor(def.harvestResource),
                    6,
                    0.85f);
            }

            GameEvents.RaiseCropPlanted(cell, cropType);
            return true;
        }

        private CropInstance CreateCropObject(Vector2Int cell, CropType cropType, CropDefinition def, float initialProgress01)
        {
            EnsureCropsRoot();

            GameObject go = new GameObject($"Crop_{cropType}_{cell.x}_{cell.y}");
            go.transform.SetParent(_cropsRoot, false);
            go.transform.position = BioGridManager.Instance.GridToWorldCenter(cell);

            CropInstance crop = go.AddComponent<CropInstance>();
            crop.Initialize(cell, cropType, def, initialProgress01);
            _activeCrops.Add(crop);
            return crop;
        }

        public void NotifyCropRemoved(CropInstance crop)
        {
            if (crop != null)
            {
                _activeCrops.Remove(crop);
            }
        }
    }
}
