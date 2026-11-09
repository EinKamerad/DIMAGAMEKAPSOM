using System.Collections.Generic;
using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;
using BioFarme.Audio;
using BioFarme.Combat;

namespace BioFarme.Factory
{
    /// <summary>
    /// The 3x3 Living Heart Core of the colony anchored at (17, 11) (world center 18.5, 12.5).
    /// Handles cardiac lub-dub animation, cyan bioluminescent aura, arterial resource pumping/receiving,
    /// defibrillation shockwaves, awakening progress toward 10 Mutagen, and colony defeat on destruction.
    /// </summary>
    public sealed class CoreHeartEntity : MonoBehaviour, IDamageable, IGridEntity, IResourceConsumer, IResourceProducer, IInteractable
    {
        private static CoreHeartEntity _instance;
        private static bool _applicationQuitting;

        public static CoreHeartEntity InstanceIfExists => _instance;

        public static CoreHeartEntity Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<CoreHeartEntity>();
                    if (_instance == null && BioGridManager.InstanceIfExists != null)
                    {
                        _instance = SpawnOnGrid(BioGridManager.InstanceIfExists, null);
                    }
                }
                return _instance;
            }
        }

        private static readonly ResourceType[] ArterialPumpCycle =
        {
            ResourceType.Biomass,
            ResourceType.Spores,
            ResourceType.BioPlasma
        };

        public Vector2Int GridPos { get; private set; }
        public StructureType Type => StructureType.LivingHeartCore;
        public StructureDefinition Definition { get; private set; }

        public float CurrentHp { get; private set; }
        public float MaxHp { get; private set; }
        public Vector2 WorldPosition => transform.position;
        public bool IsAlive => !_isDestroyed && CurrentHp > 0f;

        private SpriteRenderer _mainRenderer;
        private SpriteRenderer _auraRenderer;
        private Transform _auraTransform;

        // World-space HP bar (visible when injured)
        private GameObject _hpBarRoot;
        private Transform _hpBarFillTransform;
        private SpriteRenderer _hpBarFillRenderer;
        private const float HpBarWidth = 2.55f;
        private const float HpBarHeight = 0.20f;

        private float _heartbeatPhase;
        private float _hitFlashTimer;
        private float _packetArrivalGlow;
        private float _lastUnderAttackBannerTime = -20f;
        private float _metabolismTimer;
        private float _internalBiomassBuffer = 2f;
        private int _pumpCycleIndex;
        private float _pumpRotateTimer;
        private float _lastReportedMutagen = -1f;
        private bool _isDestroyed;

        private readonly List<IDamageable> _shockwaveEnemies = new List<IDamageable>(64);

        public static CoreHeartEntity SpawnOnGrid(BioGridManager grid, Transform parent)
        {
            if (grid == null) grid = BioGridManager.Instance;
            if (_instance != null)
            {
                return _instance;
            }

            Vector2Int anchor = grid.HeartCoreAnchor;
            StructureDefinition def = BalanceDatabase.GetStructure(StructureType.LivingHeartCore);
            Vector2Int footprint = def != null ? def.footprintSize : new Vector2Int(3, 3);

            GameObject go = new GameObject("LivingHeartCore_Entity");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            go.transform.position = grid.GetFootprintWorldCenter(anchor, footprint);

            CoreHeartEntity heart = go.AddComponent<CoreHeartEntity>();
            heart.InitializeOnGrid(grid, anchor, def);
            return heart;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                _isDestroyed = true;
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        public void InitializeOnGrid(BioGridManager grid, Vector2Int anchor, StructureDefinition def)
        {
            GridPos = anchor;
            Definition = def ?? BalanceDatabase.GetStructure(StructureType.LivingHeartCore);
            MaxHp = Definition != null && Definition.maxHp > 0f ? Definition.maxHp : 1500f;
            CurrentHp = MaxHp;
            _isDestroyed = false;

            BuildVisualHierarchy();
            UpdateHealthBarVisual();

            if (grid != null)
            {
                grid.RegisterStructure(GridPos, this);
            }

            GameEvents.RaiseCoreHealthChanged(CurrentHp, MaxHp);
            RefreshAwakeningProgress(checkVictory: false);
        }

        private void BuildVisualHierarchy()
        {
            // Cyan bioluminescent aura behind the 3x3 Living Heart
            GameObject auraGo = new GameObject("HeartCyanAura");
            auraGo.transform.SetParent(transform, false);
            auraGo.transform.localPosition = Vector3.zero;
            _auraTransform = auraGo.transform;

            _auraRenderer = auraGo.AddComponent<SpriteRenderer>();
            _auraRenderer.sprite = SpriteRegistry.GetSoftCircleSprite();
            _auraRenderer.sortingOrder = 11;
            _auraRenderer.color = new Color(0.16f, 0.92f, 1.0f, 0.30f);
            _auraTransform.localScale = Vector3.one * 4.35f;

            // Main 384x384 (3x3 units at 128 PPU) Living Heart Core sprite
            _mainRenderer = GetComponent<SpriteRenderer>();
            if (_mainRenderer == null)
            {
                _mainRenderer = gameObject.AddComponent<SpriteRenderer>();
            }
            _mainRenderer.sprite = SpriteRegistry.GetStructureSprite(StructureType.LivingHeartCore);
            _mainRenderer.sortingOrder = 14;
            _mainRenderer.color = Color.white;

            // World-space HP bar above the 3x3 Heart Core
            _hpBarRoot = new GameObject("CoreWorldHpBar");
            _hpBarRoot.transform.SetParent(transform, false);
            _hpBarRoot.transform.localPosition = new Vector3(0f, 1.78f, 0f);

            Sprite pixelSquare = SpriteRegistry.GetWhiteSquareSprite();

            GameObject borderGo = new GameObject("Border");
            borderGo.transform.SetParent(_hpBarRoot.transform, false);
            borderGo.transform.localScale = new Vector3(HpBarWidth + 0.08f, HpBarHeight + 0.08f, 1f);
            SpriteRenderer borderSr = borderGo.AddComponent<SpriteRenderer>();
            borderSr.sprite = pixelSquare;
            borderSr.color = new Color(0.05f, 0.02f, 0.08f, 0.92f);
            borderSr.sortingOrder = 60;

            GameObject bgGo = new GameObject("Background");
            bgGo.transform.SetParent(_hpBarRoot.transform, false);
            bgGo.transform.localScale = new Vector3(HpBarWidth, HpBarHeight, 1f);
            SpriteRenderer bgSr = bgGo.AddComponent<SpriteRenderer>();
            bgSr.sprite = pixelSquare;
            bgSr.color = new Color(0.16f, 0.06f, 0.10f, 0.90f);
            bgSr.sortingOrder = 61;

            GameObject fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(_hpBarRoot.transform, false);
            _hpBarFillTransform = fillGo.transform;
            _hpBarFillRenderer = fillGo.AddComponent<SpriteRenderer>();
            _hpBarFillRenderer.sprite = pixelSquare;
            _hpBarFillRenderer.color = new Color(0.24f, 0.95f, 0.56f, 0.98f);
            _hpBarFillRenderer.sortingOrder = 62;
        }

        private void OnEnable()
        {
            GameEvents.OnResourceChanged += HandleResourceChanged;
            GameEvents.OnWaveCleared += HandleWaveCleared;
            GameEvents.OnCoreDefibShockwave += HandleCoreDefibShockwave;
        }

        private void OnDisable()
        {
            GameEvents.OnResourceChanged -= HandleResourceChanged;
            GameEvents.OnWaveCleared -= HandleWaveCleared;
            GameEvents.OnCoreDefibShockwave -= HandleCoreDefibShockwave;
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
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
            if (!IsAlive) return;

            // Keep cardiac visual breathing subtly even in MainMenu preview, faster when injured or awakening!
            float visualDt = Time.deltaTime;
            float simDt = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.SimulationDeltaTime
                : Time.deltaTime;

            AnimateHeartbeatAndAura(visualDt);

            if (simDt <= 0f) return;

            TickPassiveMetabolism(simDt);
            TickArterialPumpSelection(simDt);
        }

        private void AnimateHeartbeatAndAura(float dt)
        {
            float hpRatio = MaxHp > 0f ? Mathf.Clamp01(CurrentHp / MaxHp) : 1f;
            float awakening01 = GetAwakeningProgress01();

            // Heart beats faster when injured or near full Mutagen awakening
            float beatsPerSec = Mathf.Lerp(1.35f, 0.92f, hpRatio) + awakening01 * 0.35f;
            _heartbeatPhase = (_heartbeatPhase + dt * beatsPerSec) % 1f;

            // Authentic dual-peak cardiac "lub-dub" curve
            float lub = Mathf.Exp(-Mathf.Pow((_heartbeatPhase - 0.12f) / 0.055f, 2f));
            float dub = Mathf.Exp(-Mathf.Pow((_heartbeatPhase - 0.30f) / 0.065f, 2f)) * 0.68f;
            float beat = lub + dub;

            float scaleX = 1f + beat * 0.058f - dub * 0.015f;
            float scaleY = 1f + beat * 0.048f + dub * 0.020f;
            transform.localScale = new Vector3(scaleX, scaleY, 1f);

            if (_packetArrivalGlow > 0f)
            {
                _packetArrivalGlow = Mathf.Max(0f, _packetArrivalGlow - dt * 2.5f);
            }

            if (_auraTransform != null && _auraRenderer != null)
            {
                float auraScale = 4.25f + beat * 0.45f + awakening01 * 0.55f + _packetArrivalGlow * 0.35f;
                _auraTransform.localScale = new Vector3(auraScale, auraScale, 1f);

                Color cyanBase = new Color(0.15f, 0.92f, 1.0f, 1f);
                Color awakenedTint = new Color(0.62f, 0.42f, 1.0f, 1f);
                Color auraCol = Color.Lerp(cyanBase, awakenedTint, awakening01 * 0.55f);
                auraCol.a = Mathf.Clamp01(0.22f + beat * 0.16f + awakening01 * 0.14f + _packetArrivalGlow * 0.20f);
                _auraRenderer.color = auraCol;
            }

            if (_mainRenderer != null)
            {
                if (_hitFlashTimer > 0f)
                {
                    _hitFlashTimer = Mathf.Max(0f, _hitFlashTimer - dt);
                    _mainRenderer.color = new Color(1f, 0.52f, 0.55f, 1f);
                }
                else
                {
                    float glowBoost = _packetArrivalGlow * 0.15f;
                    _mainRenderer.color = new Color(1f, 1f - (1f - hpRatio) * 0.18f + glowBoost, 1f - (1f - hpRatio) * 0.18f + glowBoost, 1f);
                }
            }
        }

        private void TickPassiveMetabolism(float simDt)
        {
            float interval = Definition != null && Definition.cycleIntervalSec > 0.5f ? Definition.cycleIntervalSec : 4.0f;
            float output = Definition != null && Definition.outputPerCycle > 0f ? Definition.outputPerCycle : 2.0f;

            _metabolismTimer += simDt;
            if (_metabolismTimer >= interval)
            {
                _metabolismTimer -= interval;
                if (ResourceManager.InstanceIfExists != null)
                {
                    ResourceManager.InstanceIfExists.Add(ResourceType.Biomass, output);
                }
                _internalBiomassBuffer = Mathf.Min(6f, _internalBiomassBuffer + 1.0f);
            }
        }

        private void TickArterialPumpSelection(float simDt)
        {
            _pumpRotateTimer += simDt;
            if (_pumpRotateTimer >= 0.7f)
            {
                _pumpRotateTimer = 0f;
                AdvancePumpResourceCycle();
            }
        }

        private void AdvancePumpResourceCycle()
        {
            ResourceManager rm = ResourceManager.InstanceIfExists;
            if (rm == null) return;

            for (int i = 0; i < ArterialPumpCycle.Length; i++)
            {
                _pumpCycleIndex = (_pumpCycleIndex + 1) % ArterialPumpCycle.Length;
                ResourceType candidate = ArterialPumpCycle[_pumpCycleIndex];
                if (candidate == ResourceType.Biomass && (_internalBiomassBuffer >= 0.5f || rm.GetRaw(candidate) >= 2f))
                {
                    return;
                }
                if (rm.GetRaw(candidate) >= 2f)
                {
                    return;
                }
            }
        }

        // --- IDamageable ---

        public void TakeDamage(float amount, Vector2 hitSource)
        {
            if (!IsAlive || amount <= 0f) return;

            CurrentHp = Mathf.Max(0f, CurrentHp - amount);
            _hitFlashTimer = 0.16f;

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(AudioKeys.SfxCoreHit, 0.95f, 0.04f);
            }

            // Crisp red damage popup above Heart Core
            Vector2 popupPos = WorldPosition + new Vector2(Random.Range(-0.55f, 0.55f), Random.Range(0.35f, 0.85f));
            int dmgInt = Mathf.Max(1, Mathf.RoundToInt(amount));
            WorldPopupManager.SpawnText(popupPos, $"-{dmgInt}", new Color(1.0f, 0.20f, 0.24f, 1f), 1.22f, amount >= 35f);

            UpdateHealthBarVisual();
            GameEvents.RaiseCoreHealthChanged(CurrentHp, MaxHp);

            if (Time.unscaledTime - _lastUnderAttackBannerTime >= 4.5f && CurrentHp > 0f)
            {
                _lastUnderAttackBannerTime = Time.unscaledTime;
                GameEvents.RaiseBannerToast(
                    $"ЯДРО ПОД АТАКОЙ! ({Mathf.CeilToInt(CurrentHp)}/{Mathf.RoundToInt(MaxHp)} ОЗ)",
                    new Color(1.0f, 0.25f, 0.28f, 1f));
            }

            if (CurrentHp <= 0f)
            {
                _isDestroyed = true;
                GameManager.Instance?.TriggerDefeat("Живое Сердце колонии было уничтожено роем мутантов!");
            }
        }

        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f || CurrentHp >= MaxHp) return;

            float before = CurrentHp;
            CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
            float actualHealed = CurrentHp - before;

            if (actualHealed >= 1f)
            {
                WorldPopupManager.SpawnHeal(WorldPosition + Vector2.up * 0.5f, actualHealed);
                _packetArrivalGlow = 0.8f;
            }

            UpdateHealthBarVisual();
            GameEvents.RaiseCoreHealthChanged(CurrentHp, MaxHp);
        }

        private void UpdateHealthBarVisual()
        {
            if (_hpBarRoot == null || _hpBarFillTransform == null) return;

            bool injured = CurrentHp < MaxHp - 0.5f && CurrentHp > 0f;
            _hpBarRoot.SetActive(injured);
            if (!injured) return;

            float ratio = Mathf.Clamp01(CurrentHp / Mathf.Max(1f, MaxHp));
            float fillW = HpBarWidth * ratio;
            _hpBarFillTransform.localScale = new Vector3(fillW, HpBarHeight, 1f);
            _hpBarFillTransform.localPosition = new Vector3(-(HpBarWidth - fillW) * 0.5f, 0f, 0f);

            if (_hpBarFillRenderer != null)
            {
                if (ratio > 0.55f)
                    _hpBarFillRenderer.color = new Color(0.22f, 0.95f, 0.62f, 0.98f);
                else if (ratio > 0.28f)
                    _hpBarFillRenderer.color = new Color(1.0f, 0.72f, 0.20f, 0.98f);
                else
                    _hpBarFillRenderer.color = new Color(1.0f, 0.22f, 0.26f, 0.98f);
            }
        }

        // --- IResourceConsumer (VeinNetworkSystem -> Living Heart Core) ---

        public bool CanAcceptResource(ResourceType resource)
        {
            return IsAlive;
        }

        public float GetResourceNeedWeight(NetworkPriorityMode priorityMode)
        {
            switch (priorityMode)
            {
                case NetworkPriorityMode.PrioritizeCore:
                    return 4.6f;
                case NetworkPriorityMode.Balanced:
                    return 1.15f;
                default:
                    return 0.55f;
            }
        }

        public float ReceiveResource(ResourceType resource, float amount)
        {
            if (amount <= 0f) return 0f;

            ResourceManager.Instance?.Add(resource, amount);
            _packetArrivalGlow = Mathf.Min(1f, _packetArrivalGlow + 0.45f);
            return amount;
        }

        // --- IResourceProducer (Living Heart Core -> Connected Turrets / Reactors / Incubators) ---

        public ResourceType OutputResourceType => ArterialPumpCycle[_pumpCycleIndex % ArterialPumpCycle.Length];

        public float StoredOutputAmount
        {
            get
            {
                ResourceType outType = OutputResourceType;
                ResourceManager rm = ResourceManager.InstanceIfExists;
                if (rm == null)
                {
                    return outType == ResourceType.Biomass ? _internalBiomassBuffer : 0f;
                }

                float stock = rm.GetRaw(outType);
                // Keep a tiny reserve so arterial pumping never starves basic seed planting
                float reserve = outType == ResourceType.Biomass ? 8f : 2f;
                float fromStock = Mathf.Max(0f, stock - reserve);
                float extra = outType == ResourceType.Biomass ? _internalBiomassBuffer : 0f;
                return Mathf.Min(4f, extra + fromStock * 0.35f);
            }
        }

        public float ExtractOutput(float maxAmount)
        {
            if (maxAmount <= 0f) return 0f;

            ResourceType outType = OutputResourceType;
            float available = StoredOutputAmount;
            float toExtract = Mathf.Min(maxAmount, available);
            if (toExtract < 0.2f)
            {
                AdvancePumpResourceCycle();
                return 0f;
            }

            float pulled = 0f;
            if (outType == ResourceType.Biomass && _internalBiomassBuffer > 0f)
            {
                float fromInternal = Mathf.Min(_internalBiomassBuffer, toExtract);
                _internalBiomassBuffer -= fromInternal;
                pulled += fromInternal;
            }

            float remainder = toExtract - pulled;
            if (remainder > 0.05f && ResourceManager.InstanceIfExists != null)
            {
                if (ResourceManager.InstanceIfExists.CanAfford(outType, remainder))
                {
                    ResourceManager.InstanceIfExists.Add(outType, -remainder);
                    pulled += remainder;
                }
            }

            AdvancePumpResourceCycle();
            return pulled;
        }

        // --- Awakening & Defibrillation Shockwave ---

        private float GetAwakeningProgress01()
        {
            if (GameManager.InstanceIfExists != null)
            {
                return Mathf.Clamp01(GameManager.InstanceIfExists.HeartAwakeningPercent / 100f);
            }
            float mut = ResourceManager.InstanceIfExists != null ? ResourceManager.InstanceIfExists.Get(ResourceType.Mutagen) : 0f;
            return Mathf.Clamp01(mut / BalanceDatabase.TargetMutagenForVictory);
        }

        private void HandleResourceChanged(ResourceType type, float newAmount, float delta)
        {
            if (type != ResourceType.Mutagen) return;
            RefreshAwakeningProgress(checkVictory: delta > 0f);
        }

        private void HandleWaveCleared(int waveNumber)
        {
            RefreshAwakeningProgress(checkVictory: true);
        }

        private void RefreshAwakeningProgress(bool checkVictory)
        {
            float currentMutagen = ResourceManager.InstanceIfExists != null
                ? ResourceManager.InstanceIfExists.Get(ResourceType.Mutagen)
                : 0f;
            float target = BalanceDatabase.TargetMutagenForVictory;
            float progress01 = Mathf.Clamp01(currentMutagen / Mathf.Max(1f, target));

            if (!Mathf.Approximately(_lastReportedMutagen, currentMutagen))
            {
                _lastReportedMutagen = currentMutagen;
                GameEvents.RaiseCoreAwakeningChanged(progress01, currentMutagen, target);
            }

            if (checkVictory)
            {
                GameManager.InstanceIfExists?.CheckVictoryCondition();
            }
        }

        private void HandleCoreDefibShockwave(Vector2 centerWorld, float radius, float enemyDamage)
        {
            if (!IsAlive) return;

            // Heal the Heart Core (+250 HP)
            Heal(250f);

            Vector2 origin = WorldPosition;
            float effectiveRadius = Mathf.Max(4.5f, radius);
            float effectiveDamage = Mathf.Max(85f, enemyDamage);

            // Spawn expanding cyan shockwave ring
            if (ProjectileManager.InstanceIfExists != null)
            {
                ProjectileManager.InstanceIfExists.SpawnWorldRingVfx(
                    origin,
                    effectiveRadius,
                    new Color(0.20f, 0.96f, 1.0f, 0.90f),
                    0.55f);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(AudioKeys.SfxHeartPulse, 1.0f, 0.03f);
            }

            // Damage and knock back all enemies within shockwave radius
            int count = WorldEntityRegistry.GetEnemiesInRadius(origin, effectiveRadius, _shockwaveEnemies);
            for (int i = 0; i < count; i++)
            {
                IDamageable target = _shockwaveEnemies[i];
                if (target == null || !target.IsAlive) continue;

                Vector2 away = target.WorldPosition - origin;
                float dist = away.magnitude;
                Vector2 knockDir = dist > 0.01f ? (away / dist) : Random.insideUnitCircle.normalized;

                if (target is EnemyAgent agent)
                {
                    float pushStrength = Mathf.Lerp(7.5f, 3.2f, Mathf.Clamp01(dist / effectiveRadius));
                    agent.ApplyKnockback(knockDir * pushStrength);
                    agent.ApplySlow(0.45f, 2.0f);
                }

                target.TakeDamage(effectiveDamage, origin);
            }
        }

        // --- IInteractable ---

        public string GetInteractionPrompt()
        {
            if (GameManager.InstanceIfExists != null &&
                GameManager.InstanceIfExists.HeartAwakeningPercent >= 99.9f &&
                GameManager.InstanceIfExists.WavesSurvived >= BalanceDatabase.TotalWaves)
            {
                return "[E] Пробудить Живое Сердце Колонии!";
            }
            return "[E] Дефибрилляция и Подкормка Ядра (Мини-игра)";
        }

        public bool CanInteract()
        {
            return IsAlive;
        }

        public void Interact()
        {
            if (!CanInteract() || GameManager.Instance == null) return;

            if (GameManager.Instance.WavesSurvived >= BalanceDatabase.TotalWaves &&
                GameManager.Instance.TryAwakenHeartCore())
            {
                return;
            }

            GameManager.Instance.OpenMiniGame(MiniGameKind.CoreDefibrillation);
        }
    }
}
