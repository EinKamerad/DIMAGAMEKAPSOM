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
    /// Mindustry-style bio-factory & colony support organisms:
    /// - SymbioticHarvester: Auto-harvests mature crops (+25% bonus yield) and auto-replants the same CropType!
    /// - BioReactor: Synthesizes +1.0 Mutagen + 5 BioPlasma from 15 Biomass + 2 BoneChitin!
    /// - MendTendril: Periodically repairs injured structures, walls, and the Core within 4.5f radius!
    /// - MutagenIncubator: Interactive Gene-Splicing laboratory ([E]) with DNA-glow bobbing + passive synthesis!
    /// </summary>
    public sealed class BioFactoryEntity : StructureBase, IResourceConsumer, IInteractable
    {
        private const float HarvesterIntervalSec = 1.5f;
        private const float MendTendrilIntervalSec = 2.0f;
        private const float MendTendrilRadius = 4.5f;
        private const float MendTendrilHealAmount = 25f;

        private const float ReactorBiomassCost = 15f;
        private const float ReactorChitinCost = 2f;
        private const float ReactorStockpileReserve = 40f;
        private const float ReactorMutagenYield = 1.0f;
        private const float ReactorPlasmaYield = 5.0f;

        private float _cycleTimer;
        private float _animPhase;
        private SpriteRenderer _glowAuraRenderer;

        private readonly List<IDamageable> _injuredScratch = new List<IDamageable>(24);
        private readonly HashSet<IDamageable> _seenDamageables = new HashSet<IDamageable>();

        protected override void OnInitialized()
        {
            _animPhase = (GridPos.x * 0.8f + GridPos.y * 0.5f);

            switch (Type)
            {
                case StructureType.SymbioticHarvester:
                    _cycleTimer = 0.6f;
                    CreateSubtleAura(new Color(0.35f, 0.95f, 0.48f, 0.16f), 1.35f);
                    break;

                case StructureType.BioReactor:
                    _cycleTimer = 2.0f;
                    CreateSubtleAura(new Color(0.95f, 0.25f, 0.75f, 0.20f), 1.45f);
                    break;

                case StructureType.MendTendril:
                    _cycleTimer = 1.0f;
                    CreateSubtleAura(new Color(0.22f, 0.96f, 0.86f, 0.18f), 1.35f);
                    break;

                case StructureType.MutagenIncubator:
                    _cycleTimer = Definition != null && Definition.cycleIntervalSec > 2f ? Definition.cycleIntervalSec : 16f;
                    CreateSubtleAura(new Color(0.92f, 0.28f, 0.92f, 0.26f), 1.60f);
                    break;
            }
        }

        private void CreateSubtleAura(Color color, float scale)
        {
            GameObject auraGo = new GameObject("FactoryGlowAura");
            auraGo.transform.SetParent(transform, false);
            auraGo.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            auraGo.transform.localScale = Vector3.one * scale;

            _glowAuraRenderer = auraGo.AddComponent<SpriteRenderer>();
            _glowAuraRenderer.sprite = SpriteRegistry.GetSoftCircleSprite();
            _glowAuraRenderer.sortingOrder = GetDesiredSortingOrder() - 2;
            _glowAuraRenderer.color = color;
        }

        protected override void OnSimulationTick(float simDt)
        {
            _animPhase += simDt * 2.5f;
            AnimateOrganismIdle();

            switch (Type)
            {
                case StructureType.SymbioticHarvester:
                    TickSymbioticHarvester(simDt);
                    break;

                case StructureType.BioReactor:
                    TickBioReactor(simDt);
                    break;

                case StructureType.MendTendril:
                    TickMendTendril(simDt);
                    break;

                case StructureType.MutagenIncubator:
                    TickMutagenIncubator(simDt);
                    break;
            }
        }

        private void AnimateOrganismIdle()
        {
            if (Type == StructureType.MutagenIncubator)
            {
                // Gentle DNA-glow vertical bobbing animation
                float bobY = Mathf.Sin(_animPhase * 1.15f) * 0.055f;
                AnimLocalOffset = new Vector3(0f, bobY, 0f);

                if (_glowAuraRenderer != null)
                {
                    float glowAlpha = 0.22f + Mathf.Sin(_animPhase * 1.8f) * 0.09f;
                    _glowAuraRenderer.color = new Color(0.94f, 0.26f, 0.90f, glowAlpha);
                }
            }
            else
            {
                float pulse = Mathf.Sin(_animPhase) * 0.03f;
                RecoilScale = Vector3.Lerp(RecoilScale, new Vector3(1f + pulse, 1f - pulse * 0.7f, 1f), 0.15f);
            }
        }

        // --- 1. SymbioticHarvester: Auto-Harvest (+25% bonus) + Auto-Replant ---

        private void TickSymbioticHarvester(float simDt)
        {
            _cycleTimer -= simDt;
            if (_cycleTimer > 0f) return;
            _cycleTimer = HarvesterIntervalSec;

            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return;

            // First check immediate 3x3 surrounding cells (dx=-1..1, dy=-1..1) as required,
            // then extend up to radius 2 (2.6f range) so adjacent multi-row farm beds are fully tended!
            if (TryHarvestOneMatureCropInRing(grid, 1))
            {
                return;
            }
            TryHarvestOneMatureCropInRing(grid, 2);
        }

        private bool TryHarvestOneMatureCropInRing(BioGridManager grid, int halfExtent)
        {
            float maxRangeSq = 2.65f * 2.65f;

            for (int dx = -halfExtent; dx <= halfExtent; dx++)
            {
                for (int dy = -halfExtent; dy <= halfExtent; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (halfExtent > 1 && Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1) continue;
                    if ((dx * dx + dy * dy) > maxRangeSq) continue;

                    Vector2Int cell = new Vector2Int(GridPos.x + dx, GridPos.y + dy);
                    if (!grid.IsInBounds(cell)) continue;

                    ICropEntity crop = WorldEntityRegistry.GetCropAt(cell) ?? grid.GetCropAt(cell);
                    if (crop == null || !crop.IsMature) continue;

                    HarvestAndReplantCell(grid, cell, crop);
                    return true;
                }
            }

            return false;
        }

        private void HarvestAndReplantCell(BioGridManager grid, Vector2Int cell, ICropEntity crop)
        {
            CropType cropType = crop.CropType;
            CropDefinition cropDef = BalanceDatabase.GetCrop(cropType);
            ResourceType yieldRes = cropDef != null ? cropDef.harvestResource : ResourceType.Biomass;

            float stockBefore = ResourceManager.InstanceIfExists != null
                ? ResourceManager.InstanceIfExists.GetRaw(yieldRes)
                : 0f;

            int rawHarvest = crop.Harvest();

            float stockAfter = ResourceManager.InstanceIfExists != null
                ? ResourceManager.InstanceIfExists.GetRaw(yieldRes)
                : 0f;

            float alreadyCredited = Mathf.Max(0f, stockAfter - stockBefore);
            int baseYield = rawHarvest > 0 ? rawHarvest : (cropDef != null ? cropDef.RollHarvestAmount() : 12);
            float totalWithBonus = Mathf.Ceil(baseYield * 1.25f); // +25% Harvester symbiosis bonus!
            float bonusOnly = Mathf.Max(1f, Mathf.Ceil(baseYield * 0.25f));

            // Safely push either full 125% (if crop.Harvest didn't credit) or the exact +25% bonus (if crop.Harvest credited base)
            float toPush = alreadyCredited >= baseYield * 0.5f
                ? bonusOnly
                : Mathf.Max(bonusOnly, totalWithBonus - alreadyCredited);

            if (VeinNetworkSystem.Instance != null)
            {
                VeinNetworkSystem.Instance.PushProducedResource(GridPos, yieldRes, toPush);
            }
            else
            {
                ResourceManager.Instance?.Add(yieldRes, toPush);
            }

            Vector2 cellWorld = grid.GridToWorldCenter(cell);
            WorldPopupManager.SpawnText(
                cellWorld + Vector2.up * 0.35f,
                $"+{Mathf.RoundToInt(bonusOnly)} Бонус",
                BalanceDatabase.GetResourceColor(yieldRes),
                0.86f,
                false);

            if (ProjectileManager.InstanceIfExists != null)
            {
                ProjectileManager.InstanceIfExists.SpawnWorldRingVfx(
                    cellWorld,
                    0.55f,
                    new Color(0.38f, 0.96f, 0.48f, 0.70f),
                    0.24f);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxHarvest, cellWorld, 0.50f, 0.08f);
            }

            RecoilScale = new Vector3(1.20f, 0.84f, 1f);

            // Auto-replant the exact same CropType if affordable!
            if (cropType != CropType.None && cropDef != null && ResourceManager.InstanceIfExists != null)
            {
                ResourceCost[] seedCosts = cropDef.GetSeedCosts();
                if (ResourceManager.InstanceIfExists.CanAfford(seedCosts) && grid.CanPlantCrop(cell))
                {
                    GameEvents.TryRequestPlantCrop(cell, cropType);
                }
            }
        }

        // --- 2. BioReactor: 15 Biomass + 2 BoneChitin -> +1.0 Mutagen + 5 BioPlasma ---

        private void TickBioReactor(float simDt)
        {
            float baseInterval = Definition != null && Definition.cycleIntervalSec > 1f ? Definition.cycleIntervalSec : 5.5f;
            float buffMult = ResourceManager.InstanceIfExists != null ? ResourceManager.InstanceIfExists.CropGrowthMultiplier : 1f;
            _cycleTimer -= simDt * Mathf.Clamp(buffMult, 0.85f, 1.6f);
            if (_cycleTimer > 0f) return;

            ResourceManager rm = ResourceManager.InstanceIfExists;
            if (rm == null)
            {
                _cycleTimer = 1.5f;
                return;
            }

            VeinNetworkSystem veins = VeinNetworkSystem.Instance;
            float bufBio = veins != null ? veins.GetNodeBufferedAmount(GridPos, ResourceType.Biomass) : 0f;
            float bufChitin = veins != null ? veins.GetNodeBufferedAmount(GridPos, ResourceType.BoneChitin) : 0f;

            float bioFromBuffer = Mathf.Min(ReactorBiomassCost, bufBio);
            float bioFromStock = ReactorBiomassCost - bioFromBuffer;

            float chitinFromBuffer = Mathf.Min(ReactorChitinCost, bufChitin);
            float chitinFromStock = ReactorChitinCost - chitinFromBuffer;

            // Only draw Biomass from colony stockpile when above the 40 Biomass safety reserve
            bool canAffordBio = bioFromStock <= 0.01f || (rm.GetRaw(ResourceType.Biomass) >= ReactorStockpileReserve + bioFromStock);
            bool canAffordChitin = chitinFromStock <= 0.01f || rm.CanAfford(ResourceType.BoneChitin, chitinFromStock);

            if (!canAffordBio || !canAffordChitin)
            {
                // Retry every 1.2s until inputs arrive
                _cycleTimer = 1.2f;
                return;
            }

            // Spend inputs
            if (bioFromBuffer > 0.01f && veins != null)
            {
                veins.TryConsumeNodeResource(GridPos, ResourceType.Biomass, bioFromBuffer);
            }
            if (bioFromStock > 0.01f)
            {
                rm.TrySpend(ResourceType.Biomass, bioFromStock);
            }

            if (chitinFromBuffer > 0.01f && veins != null)
            {
                veins.TryConsumeNodeResource(GridPos, ResourceType.BoneChitin, chitinFromBuffer);
            }
            if (chitinFromStock > 0.01f)
            {
                rm.TrySpend(ResourceType.BoneChitin, chitinFromStock);
            }

            _cycleTimer = baseInterval;

            // Synthesize +1.0 Mutagen + +5 BioPlasma!
            rm.Add(ResourceType.Mutagen, ReactorMutagenYield);
            if (veins != null)
            {
                veins.PushProducedResource(GridPos, ResourceType.BioPlasma, ReactorPlasmaYield);
            }
            else
            {
                rm.Add(ResourceType.BioPlasma, ReactorPlasmaYield);
            }

            // Glowing crimson popup "+1 Мутаген"
            WorldPopupManager.SpawnText(
                WorldPosition + Vector2.up * 0.48f,
                "+1 Мутаген",
                new Color(1.0f, 0.22f, 0.56f, 1f),
                1.18f,
                true);

            RecoilScale = new Vector3(1.22f, 0.82f, 1f);

            if (ProjectileManager.InstanceIfExists != null)
            {
                ProjectileManager.InstanceIfExists.SpawnWorldRingVfx(
                    WorldPosition,
                    1.05f,
                    new Color(0.98f, 0.24f, 0.72f, 0.75f),
                    0.35f);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxMiniGameHit, WorldPosition, 0.48f, 0.06f);
            }

            GameManager.InstanceIfExists?.CheckVictoryCondition();
        }

        // --- 3. MendTendril: Repairs Damaged Walls / Structures / Core within 4.5f ---

        private void TickMendTendril(float simDt)
        {
            _cycleTimer -= simDt;
            if (_cycleTimer > 0f) return;
            _cycleTimer = MendTendrilIntervalSec;

            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return;

            _injuredScratch.Clear();
            _seenDamageables.Clear();

            int scanRange = Mathf.CeilToInt(MendTendrilRadius);
            float radiusSq = MendTendrilRadius * MendTendrilRadius;

            for (int dx = -scanRange; dx <= scanRange; dx++)
            {
                for (int dy = -scanRange; dy <= scanRange; dy++)
                {
                    Vector2Int cell = new Vector2Int(GridPos.x + dx, GridPos.y + dy);
                    if (!grid.IsInBounds(cell)) continue;

                    IDamageable dmg = grid.GetDamageableAt(cell);
                    if (dmg == null || !dmg.IsAlive || dmg.CurrentHp >= dmg.MaxHp - 0.5f) continue;

                    Vector2 cellWorld = grid.GridToWorldCenter(cell);
                    if ((cellWorld - WorldPosition).sqrMagnitude > radiusSq) continue;

                    if (_seenDamageables.Add(dmg))
                    {
                        _injuredScratch.Add(dmg);
                    }
                }
            }

            if (_injuredScratch.Count == 0) return;

            // Pick the most injured structure by HP percentage
            IDamageable mostInjured = _injuredScratch[0];
            float lowestPct = mostInjured.CurrentHp / Mathf.Max(1f, mostInjured.MaxHp);
            for (int i = 1; i < _injuredScratch.Count; i++)
            {
                IDamageable cand = _injuredScratch[i];
                float pct = cand.CurrentHp / Mathf.Max(1f, cand.MaxHp);
                if (pct < lowestPct)
                {
                    lowestPct = pct;
                    mostInjured = cand;
                }
            }

            // Spend 1 BioPlasma if available (vein buffer first, then stockpile) for full +25 HP mend!
            bool poweredByPlasma = false;
            VeinNetworkSystem veins = VeinNetworkSystem.Instance;
            if (veins != null && veins.TryConsumeNodeResource(GridPos, ResourceType.BioPlasma, 1f))
            {
                poweredByPlasma = true;
            }
            else if (veins != null && veins.TryConsumeNodeResource(GridPos, ResourceType.Biomass, 1f))
            {
                poweredByPlasma = true;
            }
            else if (ResourceManager.InstanceIfExists != null && ResourceManager.InstanceIfExists.CanAfford(ResourceType.BioPlasma, 1f))
            {
                ResourceManager.InstanceIfExists.Add(ResourceType.BioPlasma, -1f);
                poweredByPlasma = true;
            }

            float healAmt = poweredByPlasma ? MendTendrilHealAmount : (MendTendrilHealAmount * 0.6f);
            mostInjured.Heal(healAmt);

            RecoilScale = new Vector3(0.88f, 1.18f, 1f);

            // Cyan heal particle rings at both Tendril and repaired target
            if (ProjectileManager.InstanceIfExists != null)
            {
                Color cyanHeal = new Color(0.24f, 0.98f, 0.86f, 0.80f);
                ProjectileManager.InstanceIfExists.SpawnWorldRingVfx(WorldPosition, 0.70f, cyanHeal, 0.25f);
                ProjectileManager.InstanceIfExists.SpawnWorldRingVfx(mostInjured.WorldPosition, 0.75f, cyanHeal, 0.30f);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxRepair, mostInjured.WorldPosition, 0.45f, 0.08f);
            }
        }

        // --- 4. MutagenIncubator: Interactive Gene-Splicing Lab + Passive Synthesis ---

        private void TickMutagenIncubator(float simDt)
        {
            _cycleTimer -= simDt;
            if (_cycleTimer > 0f) return;

            float interval = Definition != null && Definition.cycleIntervalSec > 2f ? Definition.cycleIntervalSec : 16f;
            float plasmaCost = Definition != null && Definition.ammoPerShot > 0f ? Definition.ammoPerShot : 4f;

            VeinNetworkSystem veins = VeinNetworkSystem.Instance;
            ResourceManager rm = ResourceManager.InstanceIfExists;
            if (rm == null)
            {
                _cycleTimer = 2f;
                return;
            }

            bool consumed = false;
            if (veins != null && veins.TryConsumeNodeResource(GridPos, ResourceType.BioPlasma, plasmaCost))
            {
                consumed = true;
            }
            else if (rm.GetRaw(ResourceType.BioPlasma) >= 25f + plasmaCost)
            {
                rm.Add(ResourceType.BioPlasma, -plasmaCost);
                consumed = true;
            }

            if (!consumed)
            {
                _cycleTimer = 2.0f;
                return;
            }

            _cycleTimer = interval;
            float mutagenOut = Definition != null && Definition.outputPerCycle > 0f ? Definition.outputPerCycle : 1.0f;
            rm.Add(ResourceType.Mutagen, mutagenOut);

            WorldPopupManager.SpawnText(
                WorldPosition + Vector2.up * 0.55f,
                $"+{mutagenOut:0.#} Мутаген",
                BalanceDatabase.GetResourceColor(ResourceType.Mutagen),
                1.15f,
                true);

            RecoilScale = new Vector3(1.18f, 0.86f, 1f);
            if (ProjectileManager.InstanceIfExists != null)
            {
                ProjectileManager.InstanceIfExists.SpawnWorldRingVfx(
                    WorldPosition,
                    1.15f,
                    new Color(0.95f, 0.30f, 0.90f, 0.75f),
                    0.35f);
            }

            GameManager.InstanceIfExists?.CheckVictoryCondition();
        }

        // --- IResourceConsumer ---

        public bool CanAcceptResource(ResourceType resource)
        {
            if (!IsAlive) return false;

            float buffered = VeinNetworkSystem.Instance != null
                ? VeinNetworkSystem.Instance.GetNodeBufferedAmount(GridPos, resource)
                : 0f;
            if (buffered >= 10f) return false;

            switch (Type)
            {
                case StructureType.BioReactor:
                    return resource == ResourceType.Biomass || resource == ResourceType.BoneChitin;

                case StructureType.MendTendril:
                    return resource == ResourceType.BioPlasma || resource == ResourceType.Biomass;

                case StructureType.MutagenIncubator:
                    return resource == ResourceType.BioPlasma;

                default:
                    return false;
            }
        }

        public float GetResourceNeedWeight(NetworkPriorityMode priorityMode)
        {
            if (!IsAlive || Type == StructureType.SymbioticHarvester) return 0f;

            if (Type == StructureType.MendTendril)
            {
                return priorityMode == NetworkPriorityMode.PrioritizeTurrets ? 3.6f : 1.2f;
            }

            switch (priorityMode)
            {
                case NetworkPriorityMode.PrioritizeReactors:
                    return 4.3f;
                case NetworkPriorityMode.Balanced:
                    return 1.5f;
                default:
                    return 0.65f;
            }
        }

        public float ReceiveResource(ResourceType resource, float amount)
        {
            // Return 0f so VeinNetworkSystem stages the delivered payload in its node buffer
            return 0f;
        }

        // --- IInteractable ---

        public string GetInteractionPrompt()
        {
            if (Type == StructureType.MutagenIncubator)
            {
                return "Био-Синтез и Мутация (Мини-игра)";
            }
            return string.Empty;
        }

        public bool CanInteract()
        {
            return IsAlive && Type == StructureType.MutagenIncubator;
        }

        public void Interact()
        {
            if (!CanInteract() || GameManager.Instance == null) return;
            GameManager.Instance.OpenMiniGame(MiniGameKind.GeneSplicing);
        }
    }
}
