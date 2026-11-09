using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using BioFarme.Data;
using BioFarme.Audio;

namespace BioFarme.Core
{
    /// <summary>
    /// Tracks all 5 colony resources (Biomass, Spores, BioPlasma, BoneChitin, Mutagen),
    /// active mini-game global buffs, and VeinNetworkSystem routing priority.
    /// </summary>
    public sealed class ResourceManager : MonoBehaviour
    {
        private static ResourceManager _instance;
        private static bool _applicationQuitting;

        public static ResourceManager InstanceIfExists => _instance;

        public static ResourceManager Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<ResourceManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[ResourceManager]");
                        _instance = go.AddComponent<ResourceManager>();
                    }
                }
                return _instance;
            }
        }

        private readonly float[] _amounts = new float[5];
        private NetworkPriorityMode _currentPriority = NetworkPriorityMode.Balanced;

        // Active Mini-Game Global Buffs
        public float CropGrowthMultiplier { get; private set; } = 1f;
        public float TurretFireRateMultiplier { get; private set; } = 1f;
        public float BuffRemainingSeconds { get; private set; }
        public bool HasActiveBuff => BuffRemainingSeconds > 0.01f;

        public NetworkPriorityMode CurrentPriority
        {
            get => _currentPriority;
            set => SetNetworkPriority(value);
        }

        // Instance event in addition to static GameEvents.OnResourceChanged for convenience
        public event Action<ResourceType, float, float> OnResourceChanged;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            ResetToStartingResources(notifyEvents: false);
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
            float simDt = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.SimulationDeltaTime
                : Time.deltaTime;

            if (simDt <= 0f) return;

            if (BuffRemainingSeconds > 0f)
            {
                BuffRemainingSeconds = Mathf.Max(0f, BuffRemainingSeconds - simDt);
                if (BuffRemainingSeconds <= 0f)
                {
                    CropGrowthMultiplier = 1f;
                    TurretFireRateMultiplier = 1f;
                    GameEvents.RaiseGlobalBuffUpdated(CropGrowthMultiplier, TurretFireRateMultiplier, 0f);
                }
            }
        }

        public void ResetToStartingResources(bool notifyEvents = true)
        {
            foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
            {
                int idx = (int)type;
                float startVal = BalanceDatabase.GetStartingResource(type);
                _amounts[idx] = startVal;
                if (notifyEvents)
                {
                    NotifyResourceChanged(type, Get(type), 0f);
                }
            }

            CropGrowthMultiplier = 1f;
            TurretFireRateMultiplier = 1f;
            BuffRemainingSeconds = 0f;
            _currentPriority = NetworkPriorityMode.Balanced;

            if (notifyEvents)
            {
                GameEvents.RaiseGlobalBuffUpdated(1f, 1f, 0f);
                GameEvents.RaiseNetworkPriorityChanged(_currentPriority);
                RaiseAwakeningProgress();
            }
        }

        /// <summary>
        /// Returns display-clean resource amount (floored integer for Biomass/Spores/BioPlasma/BoneChitin, 0.5/0.1 precision for Mutagen).
        /// </summary>
        public float Get(ResourceType type)
        {
            float raw = GetRaw(type);
            if (type == ResourceType.Mutagen)
            {
                return Mathf.Round(raw * 10f) * 0.1f;
            }
            return Mathf.Floor(raw + 0.0001f);
        }

        public int GetInt(ResourceType type)
        {
            return Mathf.FloorToInt(GetRaw(type) + 0.0001f);
        }

        public float GetRaw(ResourceType type)
        {
            int idx = (int)type;
            if (idx < 0 || idx >= _amounts.Length) return 0f;
            return _amounts[idx];
        }

        public void Set(ResourceType type, float newAmount)
        {
            int idx = (int)type;
            if (idx < 0 || idx >= _amounts.Length) return;

            float clamped = Mathf.Max(0f, newAmount);
            float delta = clamped - _amounts[idx];
            _amounts[idx] = clamped;

            NotifyResourceChanged(type, Get(type), delta);
            if (type == ResourceType.Mutagen)
            {
                RaiseAwakeningProgress();
                GameManager.InstanceIfExists?.CheckVictoryCondition();
            }
        }

        public void Add(ResourceType type, float amount)
        {
            if (Mathf.Approximately(amount, 0f)) return;

            int idx = (int)type;
            if (idx < 0 || idx >= _amounts.Length) return;

            float prev = _amounts[idx];
            float next = Mathf.Max(0f, prev + amount);
            float actualDelta = next - prev;
            _amounts[idx] = next;

            if (actualDelta > 0f && GameManager.InstanceIfExists != null)
            {
                if (type == ResourceType.Biomass)
                {
                    GameManager.InstanceIfExists.RecordBiomassHarvested(actualDelta);
                }
                else if (type == ResourceType.Mutagen)
                {
                    GameManager.InstanceIfExists.RecordMutagenSynthesized(actualDelta);
                }
            }

            NotifyResourceChanged(type, Get(type), actualDelta);

            if (type == ResourceType.Mutagen)
            {
                RaiseAwakeningProgress();
                GameManager.InstanceIfExists?.CheckVictoryCondition();
            }
        }

        public bool CanAfford(ResourceType type, float costAmount)
        {
            if (costAmount <= 0f) return true;
            return GetRaw(type) + 0.001f >= costAmount;
        }

        public bool CanAfford(ResourceCost cost)
        {
            return CanAfford(cost.resource, cost.amount);
        }

        public bool CanAfford(ResourceCost[] costs)
        {
            if (costs == null || costs.Length == 0) return true;
            for (int i = 0; i < costs.Length; i++)
            {
                if (!CanAfford(costs[i].resource, costs[i].amount))
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanAffordList(IReadOnlyList<ResourceCost> costs)
        {
            if (costs == null || costs.Count == 0) return true;
            for (int i = 0; i < costs.Count; i++)
            {
                if (!CanAfford(costs[i].resource, costs[i].amount))
                {
                    return false;
                }
            }
            return true;
        }

        public bool TrySpend(ResourceType type, float costAmount)
        {
            if (costAmount <= 0f) return true;
            if (!CanAfford(type, costAmount))
            {
                GameEvents.RaiseInsufficientResource(type);
                return false;
            }

            Add(type, -costAmount);
            return true;
        }

        public bool TrySpend(ResourceCost cost)
        {
            return TrySpend(cost.resource, cost.amount);
        }

        public bool TrySpend(ResourceCost[] costs)
        {
            if (costs == null || costs.Length == 0) return true;

            for (int i = 0; i < costs.Length; i++)
            {
                if (!CanAfford(costs[i].resource, costs[i].amount))
                {
                    GameEvents.RaiseInsufficientResource(costs[i].resource);
                    return false;
                }
            }

            for (int i = 0; i < costs.Length; i++)
            {
                if (costs[i].amount > 0f)
                {
                    Add(costs[i].resource, -costs[i].amount);
                }
            }

            return true;
        }

        public void RefundPercentage(ResourceCost[] costs, float percentage = BalanceDatabase.DeconstructRefundFraction)
        {
            if (costs == null || costs.Length == 0) return;
            float clampedRatio = Mathf.Clamp01(percentage);
            if (clampedRatio <= 0f) return;

            for (int i = 0; i < costs.Length; i++)
            {
                if (costs[i].amount <= 0f) continue;
                float refund = costs[i].resource == ResourceType.Mutagen
                    ? Mathf.Round(costs[i].amount * clampedRatio * 2f) * 0.5f
                    : Mathf.Max(1f, Mathf.Round(costs[i].amount * clampedRatio));
                Add(costs[i].resource, refund);
            }
        }

        // --- Mini-Game Global Buffs ---

        /// <summary>
        /// Grants Mutagen and activates colony-wide crop & turret overdrive after Gene-Splicing mini-game.
        /// scoreTier: 0 = partial/consolation, 1 = standard success, 2+ = perfect splice!
        /// </summary>
        public void TriggerGeneSpliceReward(int scoreTier)
        {
            float mutagenBonus;
            float duration;
            float cropMult;
            float turretMult;

            if (scoreTier <= 0)
            {
                mutagenBonus = 0.5f;
                duration = 20f;
                cropMult = 1.35f;
                turretMult = 1.15f;
            }
            else if (scoreTier == 1)
            {
                mutagenBonus = 1.5f;
                duration = 45f;
                cropMult = 2.0f;
                turretMult = 1.40f;
            }
            else
            {
                mutagenBonus = 2.5f;
                duration = 55f;
                cropMult = 2.25f;
                turretMult = 1.55f;
            }

            CropGrowthMultiplier = Mathf.Max(CropGrowthMultiplier, cropMult);
            TurretFireRateMultiplier = Mathf.Max(TurretFireRateMultiplier, turretMult);
            BuffRemainingSeconds = Mathf.Max(BuffRemainingSeconds, duration);

            Add(ResourceType.Mutagen, mutagenBonus);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(AudioKeys.SfxMiniGameSuccess, 0.9f, 0.03f);
            }

            GameEvents.RaiseGlobalBuffUpdated(CropGrowthMultiplier, TurretFireRateMultiplier, BuffRemainingSeconds);
            GameEvents.RaiseBannerToast(
                $"Гено-сплайсинг успешен! +{mutagenBonus:0.#} Мутагена, рост культур x{CropGrowthMultiplier:0.#} ({Mathf.RoundToInt(BuffRemainingSeconds)}с)",
                BalanceDatabase.GetResourceColor(ResourceType.Mutagen));
        }

        /// <summary>
        /// Grants BioPlasma/Mutagen, heals Living Heart Core, emits shockwave, and boosts turret fire rate after Core Defibrillation.
        /// </summary>
        public void TriggerCoreDefibReward(int scoreTier)
        {
            float plasmaBonus = scoreTier >= 2 ? 60f : (scoreTier == 1 ? 35f : 15f);
            float mutagenBonus = scoreTier >= 2 ? 2.0f : (scoreTier == 1 ? 1.0f : 0.5f);
            float duration = scoreTier >= 1 ? 45f : 20f;
            float turretMult = scoreTier >= 2 ? 1.65f : 1.50f;
            float shockwaveDmg = scoreTier >= 2 ? 180f : 110f;

            TurretFireRateMultiplier = Mathf.Max(TurretFireRateMultiplier, turretMult);
            CropGrowthMultiplier = Mathf.Max(CropGrowthMultiplier, 1.35f);
            BuffRemainingSeconds = Mathf.Max(BuffRemainingSeconds, duration);

            Add(ResourceType.BioPlasma, plasmaBonus);
            Add(ResourceType.Mutagen, mutagenBonus);

            Vector2 coreCenter = new Vector2(18.5f, 12.5f);
            if (Grid.BioGridManager.InstanceIfExists != null)
            {
                coreCenter = Grid.BioGridManager.InstanceIfExists.HeartCoreCenterWorld;
                IDamageable coreDmg = Grid.BioGridManager.InstanceIfExists.GetDamageableAt(Grid.BioGridManager.InstanceIfExists.HeartCoreCenterGrid);
                if (coreDmg != null && coreDmg.IsAlive)
                {
                    float healAmt = scoreTier >= 2 ? 450f : 280f;
                    coreDmg.Heal(healAmt);
                }
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(AudioKeys.SfxHeartPulse, 1.0f, 0.02f);
            }

            GameEvents.RaiseCoreDefibShockwave(coreCenter, 8.5f, shockwaveDmg);
            GameEvents.RaiseGlobalBuffUpdated(CropGrowthMultiplier, TurretFireRateMultiplier, BuffRemainingSeconds);
            GameEvents.RaiseBannerToast(
                $"Сердце реанимировано! Ударная волна + скорострельность турелей x{TurretFireRateMultiplier:0.##}!",
                BalanceDatabase.GetResourceColor(ResourceType.BioPlasma));
        }

        // --- Network Priority ---

        public void SetNetworkPriority(NetworkPriorityMode mode)
        {
            if (_currentPriority == mode) return;
            _currentPriority = mode;
            GameEvents.RaiseNetworkPriorityChanged(_currentPriority);
        }

        public NetworkPriorityMode CycleNetworkPriority()
        {
            int next = ((int)_currentPriority + 1) % 4;
            SetNetworkPriority((NetworkPriorityMode)next);
            return _currentPriority;
        }

        public static string GetPriorityDisplayNameRu(NetworkPriorityMode mode)
        {
            switch (mode)
            {
                case NetworkPriorityMode.Balanced: return "Баланс сети";
                case NetworkPriorityMode.PrioritizeTurrets: return "Приоритет: Турели";
                case NetworkPriorityMode.PrioritizeReactors: return "Приоритет: Реакторы";
                case NetworkPriorityMode.PrioritizeCore: return "Приоритет: Сердце";
                default: return mode.ToString();
            }
        }

        public static string FormatCostsRu(ResourceCost[] costs)
        {
            if (costs == null || costs.Length == 0) return "Бесплатно";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < costs.Length; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append($"{costs[i].amount:0.#} {BalanceDatabase.GetResourceNameRu(costs[i].resource)}");
            }
            return sb.ToString();
        }

        private void NotifyResourceChanged(ResourceType type, float displayAmount, float delta)
        {
            OnResourceChanged?.Invoke(type, displayAmount, delta);
            GameEvents.RaiseResourceChanged(type, displayAmount, delta);
        }

        private void RaiseAwakeningProgress()
        {
            float currentMutagen = Get(ResourceType.Mutagen);
            float target = BalanceDatabase.TargetMutagenForVictory;
            float progress01 = Mathf.Clamp01(currentMutagen / Mathf.Max(1f, target));
            GameEvents.RaiseCoreAwakeningChanged(progress01, currentMutagen, target);
        }
    }
}
