using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;
using BioFarme.Audio;

namespace BioFarme.Farming
{
    /// <summary>
    /// Cultivated biopunk flora entity growing on a TilledBed / WateredBed grid cell.
    /// Implements ICropEntity (for player tools & SymbioticHarvester) and IInteractable (for [E] harvest).
    /// </summary>
    public sealed class CropInstance : MonoBehaviour, ICropEntity, IInteractable
    {
        public Vector2Int GridPos { get; private set; }
        public CropType CropType { get; private set; }
        public bool IsMature => _growthProgress >= 1.0f;
        public float GrowthProgress => _growthProgress;

        private CropDefinition _def;
        private float _growthProgress;
        private int _displayedStage = -1;
        private bool _harvested;
        private bool _registered;

        private Transform _visualTransform;
        private SpriteRenderer _cropRenderer;
        private SpriteRenderer _auraRenderer;
        private SpriteRenderer _moistureDotRenderer;

        private float _animPhase;
        private float _stagePopTimer;

        public void Initialize(Vector2Int cell, CropType cropType, CropDefinition def, float initialProgress01 = 0f)
        {
            GridPos = cell;
            CropType = cropType;
            _def = def ?? BalanceDatabase.GetCrop(cropType);
            _growthProgress = Mathf.Clamp01(initialProgress01);
            _animPhase = (cell.x * 1.37f + cell.y * 2.19f) % (Mathf.PI * 2f);
            _stagePopTimer = 0.22f;

            BuildVisualHierarchy();

            WorldEntityRegistry.RegisterCrop(cell, this);
            if (BioGridManager.InstanceIfExists != null)
            {
                BioGridManager.InstanceIfExists.SetCropOccupancy(cell, true, this);
            }
            _registered = true;

            RefreshStageVisual(force: true);
            UpdateIndicators(0f);
        }

        private void BuildVisualHierarchy()
        {
            // Backlight bioluminescent aura (pulses when mature or irrigated)
            GameObject auraGo = new GameObject("BiolumAura");
            auraGo.transform.SetParent(transform, false);
            auraGo.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            _auraRenderer = auraGo.AddComponent<SpriteRenderer>();
            _auraRenderer.sprite = SpriteRegistry.GetSoftCircleSprite();
            _auraRenderer.sortingOrder = 5;

            // Main flora sprite container (for squash/stretch & mature bobbing)
            GameObject visGo = new GameObject("CropSprite");
            visGo.transform.SetParent(transform, false);
            _visualTransform = visGo.transform;
            _cropRenderer = visGo.AddComponent<SpriteRenderer>();
            _cropRenderer.sortingOrder = 6;

            // Subtle hydration status droplet indicator in corner of bed
            GameObject dotGo = new GameObject("HydrationDot");
            dotGo.transform.SetParent(transform, false);
            dotGo.transform.localPosition = new Vector3(0.30f, -0.28f, 0f);
            dotGo.transform.localScale = Vector3.one * 0.16f;
            _moistureDotRenderer = dotGo.AddComponent<SpriteRenderer>();
            _moistureDotRenderer.sprite = SpriteRegistry.GetSoftCircleSprite();
            _moistureDotRenderer.sortingOrder = 7;
        }

        private void Update()
        {
            if (_harvested) return;

            float simDt = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.SimulationDeltaTime
                : Time.deltaTime;

            if (!IsMature && simDt > 0f)
            {
                AdvanceGrowth(simDt);
            }

            float visualDt = simDt > 0f ? simDt : Time.unscaledDeltaTime * 0.35f;
            UpdateIndicators(visualDt);
        }

        private void AdvanceGrowth(float simDt)
        {
            float duration = (_def != null && _def.growthDurationSec > 0.5f) ? _def.growthDurationSec : 15f;
            float baseRate = 1f / duration;

            BioGridManager grid = BioGridManager.InstanceIfExists;
            bool isWatered = grid != null && grid.IsTileWatered(GridPos);
            bool isFertile = grid != null && grid.IsTileFertile(GridPos);

            float waterMult = isWatered ? 1.45f : 0.65f;
            float fertileMult = isFertile ? (_def != null ? _def.fertileBonusMultiplier : 1.25f) : 1.0f;
            float buffMult = ResourceManager.InstanceIfExists != null ? ResourceManager.InstanceIfExists.CropGrowthMultiplier : 1.0f;

            WeatherType currentWeather = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.CurrentWeather
                : WeatherType.CalmSymbiosis;
            WeatherDefinition weatherDef = BalanceDatabase.GetWeather(currentWeather);
            float weatherMult = weatherDef != null ? weatherDef.cropGrowthMultiplier : 1.0f;

            float prev = _growthProgress;
            _growthProgress = Mathf.Clamp01(_growthProgress + baseRate * waterMult * fertileMult * buffMult * weatherMult * simDt);

            if (prev < 1.0f && _growthProgress >= 1.0f)
            {
                _stagePopTimer = 0.28f;
                if (BioWeatherSystem.InstanceIfExists != null)
                {
                    Color resCol = BalanceDatabase.GetResourceColor(_def != null ? _def.harvestResource : ResourceType.Spores);
                    BioWeatherSystem.InstanceIfExists.SpawnPuffBurst(transform.position, resCol, 6, 0.9f);
                }
            }

            RefreshStageVisual(force: false);
        }

        private void RefreshStageVisual(bool force)
        {
            int targetStage = _growthProgress >= 1.0f
                ? 3
                : Mathf.Clamp(Mathf.FloorToInt(_growthProgress * 3.0f), 0, 2);

            if (!force && targetStage == _displayedStage) return;

            if (_displayedStage >= 0 && targetStage > _displayedStage)
            {
                _stagePopTimer = 0.22f;
            }

            _displayedStage = targetStage;
            if (_cropRenderer != null)
            {
                _cropRenderer.sprite = SpriteRegistry.GetCropStageSprite(CropType, targetStage);
            }
        }

        private void UpdateIndicators(float dt)
        {
            _animPhase += dt * (IsMature ? 3.8f : 2.1f);

            float scaleX = 1f;
            float scaleY = 1f;
            float offsetY = 0f;

            if (_stagePopTimer > 0f)
            {
                _stagePopTimer = Mathf.Max(0f, _stagePopTimer - Time.deltaTime);
                float t = _stagePopTimer / 0.28f;
                float bounce = Mathf.Sin(t * Mathf.PI) * 0.22f;
                scaleX = 1f - bounce * 0.45f;
                scaleY = 1f + bounce;
            }

            bool watered = BioGridManager.InstanceIfExists != null && BioGridManager.InstanceIfExists.IsTileWatered(GridPos);

            if (IsMature)
            {
                float pulse = Mathf.Sin(_animPhase);
                offsetY = (pulse + 1f) * 0.032f;
                scaleX *= 1f + pulse * 0.045f;
                scaleY *= 1f - pulse * 0.035f;

                if (_auraRenderer != null)
                {
                    Color yieldCol = BalanceDatabase.GetResourceColor(_def != null ? _def.harvestResource : ResourceType.Spores);
                    Color goldGreen = Color.Lerp(new Color(0.55f, 1.0f, 0.42f, 1f), yieldCol, 0.45f);
                    goldGreen.a = 0.34f + 0.16f * pulse;
                    _auraRenderer.color = goldGreen;
                    float auraScale = 0.92f + 0.14f * pulse;
                    _auraRenderer.transform.localScale = new Vector3(auraScale, auraScale, 1f);
                }

                if (_moistureDotRenderer != null)
                {
                    _moistureDotRenderer.enabled = false;
                }
            }
            else
            {
                // Gentle seedling sway
                float sway = Mathf.Sin(_animPhase) * 0.02f;
                scaleY *= 1f + sway;

                if (_auraRenderer != null)
                {
                    if (watered)
                    {
                        _auraRenderer.color = new Color(0.20f, 0.85f, 0.98f, 0.15f);
                        _auraRenderer.transform.localScale = new Vector3(0.68f, 0.68f, 1f);
                    }
                    else
                    {
                        _auraRenderer.color = new Color(0f, 0f, 0f, 0f);
                    }
                }

                if (_moistureDotRenderer != null)
                {
                    _moistureDotRenderer.enabled = true;
                    if (watered)
                    {
                        _moistureDotRenderer.color = new Color(0.25f, 0.92f, 1.0f, 0.72f);
                        _moistureDotRenderer.transform.localScale = Vector3.one * 0.15f;
                    }
                    else
                    {
                        float blink = 0.5f + 0.5f * Mathf.Sin(_animPhase * 1.6f);
                        _moistureDotRenderer.color = new Color(1.0f, 0.68f, 0.22f, 0.35f + 0.40f * blink);
                        _moistureDotRenderer.transform.localScale = Vector3.one * (0.14f + 0.04f * blink);
                    }
                }
            }

            if (_visualTransform != null)
            {
                _visualTransform.localPosition = new Vector3(0f, offsetY, 0f);
                _visualTransform.localScale = new Vector3(scaleX, scaleY, 1f);
            }
        }

        public int Harvest()
        {
            if (_harvested || !IsMature) return 0;
            _harvested = true;

            Vector2 worldPos = transform.position;
            int harvestAmount = _def != null ? _def.RollHarvestAmount() : 12;
            ResourceType yieldResource = _def != null ? _def.harvestResource : ResourceType.Spores;

            if (ResourceManager.Instance != null)
            {
                ResourceManager.Instance.Add(yieldResource, harvestAmount);
            }

            // Bonus Spores seed return chance so the colony never soft-locks out of seeds
            int bonusSpores = 0;
            if (yieldResource != ResourceType.Spores)
            {
                if (Random.value <= 0.78f)
                {
                    bonusSpores = Random.Range(4, 9);
                }
            }
            else if (Random.value <= 0.35f)
            {
                bonusSpores = Random.Range(2, 5);
            }

            if (bonusSpores > 0 && ResourceManager.Instance != null)
            {
                ResourceManager.Instance.Add(ResourceType.Spores, bonusSpores);
            }

            if (GameManager.InstanceIfExists != null)
            {
                GameManager.InstanceIfExists.RecordCropHarvested();
            }

            GameEvents.RaiseCropHarvested(GridPos, CropType, yieldResource, harvestAmount);
            WorldPopupManager.SpawnResourceGain(worldPos, yieldResource, harvestAmount);

            if (bonusSpores > 0 && yieldResource != ResourceType.Spores)
            {
                WorldPopupManager.SpawnResourceGain(worldPos + new Vector2(0f, 0.32f), ResourceType.Spores, bonusSpores);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxHarvest, worldPos, 0.85f, 0.07f);
            }

            if (BioWeatherSystem.InstanceIfExists != null)
            {
                BioWeatherSystem.InstanceIfExists.SpawnPuffBurst(
                    worldPos,
                    BalanceDatabase.GetResourceColor(yieldResource),
                    10,
                    1.35f);
            }

            ClearRegistration();
            Destroy(gameObject);
            return harvestAmount;
        }

        public string GetInteractionPrompt()
        {
            if (!CanInteract()) return string.Empty;
            string cropName = _def != null ? _def.displayName : CropType.ToString();
            return $"[E] Собрать: {cropName}";
        }

        public bool CanInteract()
        {
            return !_harvested && IsMature;
        }

        public void Interact()
        {
            if (CanInteract())
            {
                Harvest();
            }
        }

        private void ClearRegistration()
        {
            if (!_registered) return;
            _registered = false;

            if (ReferenceEquals(WorldEntityRegistry.GetCropAt(GridPos), this))
            {
                WorldEntityRegistry.UnregisterCrop(GridPos);
            }

            if (BioGridManager.InstanceIfExists != null &&
                ReferenceEquals(BioGridManager.InstanceIfExists.GetCropAt(GridPos), this))
            {
                BioGridManager.InstanceIfExists.SetCropOccupancy(GridPos, false, null);
            }

            if (CropSystem.InstanceIfExists != null)
            {
                CropSystem.InstanceIfExists.NotifyCropRemoved(this);
            }
        }

        private void OnDestroy()
        {
            ClearRegistration();
        }
    }
}
