using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;
using BioFarme.Audio;
using BioFarme.Combat;

namespace BioFarme.Factory
{
    /// <summary>
    /// Handles PvZ-meets-Mindustry organic combat turrets (FlytrapTurret, SporeFlowerTurret, BileCannonTurret)
    /// and the bioluminescent PulseBulbGenerator.
    /// Combat turrets fire at base 1.0x rate standalone and overdrive to 1.6x fire rate + 35% empowered damage
    /// when supplied with ammo pulses through VeinNetworkSystem!
    /// </summary>
    public sealed class BioTurretEntity : StructureBase, IResourceConsumer
    {
        private Sprite _idleSprite;
        private Sprite _activeOrAttackSprite;
        private SpriteRenderer _overdriveAuraRenderer;

        private float _actionCooldown;
        private float _attackPoseTimer;
        private float _idleSwayPhase;
        private float _ammoFedGlowTimer;

        public bool IsGenerator => Type == StructureType.PulseBulbGenerator;

        protected override void OnInitialized()
        {
            _idleSwayPhase = (GridPos.x * 0.7f + GridPos.y * 1.1f);
            ResolveStateSprites();
            CreateOverdriveAura();

            if (IsGenerator)
            {
                float interval = Definition != null && Definition.cycleIntervalSec > 0.5f ? Definition.cycleIntervalSec : 5.0f;
                _actionCooldown = interval * 0.65f;
            }
            else
            {
                _actionCooldown = 0.15f;
            }
        }

        private void ResolveStateSprites()
        {
            switch (Type)
            {
                case StructureType.FlytrapTurret:
                    _idleSprite = SpriteRegistry.Get("Turret_Flytrap_Idle");
                    _activeOrAttackSprite = SpriteRegistry.Get("Turret_Flytrap_Attack");
                    break;

                case StructureType.SporeFlowerTurret:
                    _idleSprite = SpriteRegistry.Get("Turret_SporeFlower_Idle");
                    _activeOrAttackSprite = SpriteRegistry.Get("Turret_SporeFlower_Attack");
                    break;

                case StructureType.BileCannonTurret:
                    _idleSprite = SpriteRegistry.Get("Turret_BileCannon_Idle");
                    _activeOrAttackSprite = _idleSprite;
                    break;

                case StructureType.PulseBulbGenerator:
                    _idleSprite = SpriteRegistry.Get("Turret_PulseBulb_Idle");
                    _activeOrAttackSprite = SpriteRegistry.Get("Turret_PulseBulb_Active");
                    break;

                default:
                    _idleSprite = SpriteRegistry.GetStructureSprite(Type);
                    _activeOrAttackSprite = _idleSprite;
                    break;
            }

            if (MainRenderer != null && _idleSprite != null)
            {
                MainRenderer.sprite = _idleSprite;
            }
        }

        private void CreateOverdriveAura()
        {
            GameObject auraGo = new GameObject("TurretVeinAura");
            auraGo.transform.SetParent(transform, false);
            auraGo.transform.localPosition = new Vector3(0f, -0.08f, 0f);
            auraGo.transform.localScale = Vector3.one * 1.15f;

            _overdriveAuraRenderer = auraGo.AddComponent<SpriteRenderer>();
            _overdriveAuraRenderer.sprite = SpriteRegistry.GetSoftCircleSprite();
            _overdriveAuraRenderer.sortingOrder = GetDesiredSortingOrder() - 2;
            _overdriveAuraRenderer.color = new Color(0.25f, 0.95f, 0.75f, 0f);
        }

        protected override void OnSimulationTick(float simDt)
        {
            _idleSwayPhase += simDt * 2.8f;

            if (_attackPoseTimer > 0f)
            {
                _attackPoseTimer = Mathf.Max(0f, _attackPoseTimer - simDt);
                if (_attackPoseTimer <= 0f && MainRenderer != null && _idleSprite != null)
                {
                    MainRenderer.sprite = _idleSprite;
                    AnimLocalOffset = Vector3.zero;
                }
            }
            else
            {
                // Subtle living plant breathing sway while idle
                float breathe = Mathf.Sin(_idleSwayPhase) * 0.028f;
                RecoilScale = Vector3.Lerp(RecoilScale, new Vector3(1f - breathe * 0.5f, 1f + breathe, 1f), simDt * 8f);
                AnimLocalOffset = Vector3.Lerp(AnimLocalOffset, Vector3.zero, simDt * 10f);
            }

            if (_ammoFedGlowTimer > 0f)
            {
                _ammoFedGlowTimer = Mathf.Max(0f, _ammoFedGlowTimer - simDt);
            }

            UpdateOverdriveAuraVisual();

            if (IsGenerator)
            {
                TickPulseBulbGenerator(simDt);
            }
            else
            {
                TickCombatTurret(simDt);
            }
        }

        private void UpdateOverdriveAuraVisual()
        {
            if (_overdriveAuraRenderer == null) return;

            if (IsGenerator)
            {
                bool connected = VeinNetworkSystem.Instance != null && VeinNetworkSystem.Instance.IsNodeConnected(GridPos);
                float alpha = connected ? (0.18f + Mathf.Sin(_idleSwayPhase * 1.4f) * 0.06f) : 0.05f;
                _overdriveAuraRenderer.color = new Color(0.20f, 0.92f, 1.0f, alpha);
                return;
            }

            ResourceType ammoRes = Definition != null ? Definition.powerOrAmmoResource : ResourceType.Biomass;
            float minAmmo = Definition != null ? Mathf.Max(0.1f, Definition.ammoPerShot) : 0.4f;
            float buffered = VeinNetworkSystem.Instance != null
                ? VeinNetworkSystem.Instance.GetNodeBufferedAmount(GridPos, ammoRes)
                : 0f;

            bool globalBuff = ResourceManager.InstanceIfExists != null && ResourceManager.InstanceIfExists.TurretFireRateMultiplier > 1.05f;
            bool empoweredReady = buffered >= minAmmo || _ammoFedGlowTimer > 0f;

            if (empoweredReady || globalBuff)
            {
                Color c = BalanceDatabase.GetResourceColor(ammoRes);
                float pulse = 0.22f + Mathf.Sin(_idleSwayPhase * 2.2f) * 0.07f;
                _overdriveAuraRenderer.color = new Color(c.r, c.g, c.b, pulse);
            }
            else
            {
                _overdriveAuraRenderer.color = new Color(0.2f, 0.8f, 0.5f, 0f);
            }
        }

        private void TickPulseBulbGenerator(float simDt)
        {
            float baseInterval = Definition != null && Definition.cycleIntervalSec > 0.5f ? Definition.cycleIntervalSec : 5.0f;
            float speedMult = ResourceManager.InstanceIfExists != null ? ResourceManager.InstanceIfExists.CropGrowthMultiplier : 1f;
            float effectiveDt = simDt * Mathf.Clamp(speedMult, 0.8f, 1.8f);

            _actionCooldown -= effectiveDt;
            if (_actionCooldown > 0f) return;

            _actionCooldown = baseInterval;

            float fullPlasma = Definition != null && Definition.outputPerCycle > 0f ? Definition.outputPerCycle : 3.0f;
            const float bonusBiomass = 1.0f;

            bool veinConnected = VeinNetworkSystem.Instance != null && VeinNetworkSystem.Instance.IsNodeConnected(GridPos);
            if (veinConnected)
            {
                // 100% full output pumped as glowing packets into the Vein network!
                VeinNetworkSystem.Instance.PushProducedResource(GridPos, ResourceType.BioPlasma, fullPlasma);
                VeinNetworkSystem.Instance.PushProducedResource(GridPos, ResourceType.Biomass, bonusBiomass);

                WorldPopupManager.SpawnText(
                    WorldPosition + Vector2.up * 0.38f,
                    $"+{Mathf.RoundToInt(fullPlasma)} Плазма",
                    BalanceDatabase.GetResourceColor(ResourceType.BioPlasma),
                    0.92f,
                    false);
            }
            else
            {
                // 50% standalone trickle directly to ResourceManager so new players always get value
                float halfPlasma = Mathf.Max(1f, Mathf.Round(fullPlasma * 0.5f));
                ResourceManager.Instance?.Add(ResourceType.BioPlasma, halfPlasma);
                ResourceManager.Instance?.Add(ResourceType.Biomass, bonusBiomass);

                WorldPopupManager.SpawnText(
                    WorldPosition + Vector2.up * 0.38f,
                    $"+{Mathf.RoundToInt(halfPlasma)} Плазма",
                    BalanceDatabase.GetResourceColor(ResourceType.BioPlasma),
                    0.85f,
                    false);
            }

            // Swap to glowing Active sprite & squash-stretch pulse
            if (MainRenderer != null && _activeOrAttackSprite != null)
            {
                MainRenderer.sprite = _activeOrAttackSprite;
            }
            _attackPoseTimer = 1.15f;
            RecoilScale = new Vector3(1.18f, 0.86f, 1f);

            if (ProjectileManager.InstanceIfExists != null)
            {
                ProjectileManager.InstanceIfExists.SpawnWorldRingVfx(
                    WorldPosition,
                    0.85f,
                    new Color(0.22f, 0.94f, 1.0f, 0.55f),
                    0.30f);
            }
        }

        private void TickCombatTurret(float simDt)
        {
            if (_actionCooldown > 0f)
            {
                _actionCooldown -= simDt;
                if (_actionCooldown > 0f) return;
            }

            float range = Definition != null && Definition.range > 0.5f ? Definition.range : 4.2f;
            IDamageable target = WorldEntityRegistry.GetClosestEnemy(WorldPosition, range);
            if (target == null || !target.IsAlive)
            {
                // Re-scan every 0.08s when idle to keep CPU ultra-clean
                _actionCooldown = 0.08f;
                return;
            }

            FireAtTarget(target);
        }

        private void FireAtTarget(IDamageable target)
        {
            StructureDefinition def = Definition;
            float baseFireRate = def != null && def.fireRate > 0.05f ? def.fireRate : 1.0f;
            float baseDamage = def != null && def.damage > 0f ? def.damage : 20f;
            ResourceType ammoRes = def != null ? def.powerOrAmmoResource : ResourceType.Biomass;
            float ammoCost = def != null ? def.ammoPerShot : 0.4f;

            // Query VeinNetworkSystem for ammo consumption:
            // ConsumeTurretShotAndGetMultiplier returns 1.25f if vein buffer supplied ammo, or 0.70f if dry.
            // Per design: turrets shoot at 1.0x base rate without veins, and supercharge to 1.6x fire rate + 35% empowered damage with veins!
            float rawVeinMult = VeinNetworkSystem.Instance != null
                ? VeinNetworkSystem.Instance.ConsumeTurretShotAndGetMultiplier(GridPos, ammoRes, ammoCost)
                : 0.70f;

            bool isEmpowered = rawVeinMult > 1.05f;
            float veinFireRateMult = isEmpowered ? 1.60f : 1.00f;
            float globalBuffMult = ResourceManager.InstanceIfExists != null
                ? Mathf.Max(0.5f, ResourceManager.InstanceIfExists.TurretFireRateMultiplier)
                : 1.0f;

            float effectiveFireRate = Mathf.Max(0.15f, baseFireRate * veinFireRateMult * globalBuffMult);
            _actionCooldown = 1f / effectiveFireRate;

            float shotDamage = baseDamage * (isEmpowered ? 1.35f : 1.0f);
            float projSpeed = def != null && def.projectileSpeed > 1f ? def.projectileSpeed : 10f;
            float splashRadius = def != null ? def.splashRadius : 0f;
            float slowFactor = def != null ? def.slowFactor : 0f;

            Vector2 toTarget = target.WorldPosition - WorldPosition;
            Vector2 dir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.right;

            // Face target horizontally (curated turret PNGs face Right (+X) by default)
            if (MainRenderer != null)
            {
                if (Mathf.Abs(toTarget.x) > 0.05f)
                {
                    MainRenderer.flipX = toTarget.x < 0f;
                }
                if (_activeOrAttackSprite != null)
                {
                    MainRenderer.sprite = _activeOrAttackSprite;
                }
            }

            // Juicy squash-and-stretch recoil + forward snap
            _attackPoseTimer = Mathf.Min(0.22f, _actionCooldown * 0.60f);
            RecoilScale = Type == StructureType.BileCannonTurret
                ? new Vector3(1.30f, 0.76f, 1f)
                : new Vector3(1.22f, 0.82f, 1f);
            AnimLocalOffset = (Vector3)(dir * 0.08f);

            // Audio feedback
            if (AudioManager.Instance != null)
            {
                string sfxKey = Type == StructureType.BileCannonTurret
                    ? AudioKeys.SfxBileCannon
                    : (Type == StructureType.SporeFlowerTurret ? AudioKeys.SfxSporeMortar : AudioKeys.SfxTurretShoot);
                float vol = Type == StructureType.BileCannonTurret ? 0.82f : 0.68f;
                AudioManager.Instance.PlaySfxAtPosition(sfxKey, WorldPosition, vol, 0.07f);
            }

            // Spawn projectile
            Vector2 muzzlePos = WorldPosition + dir * 0.34f + Vector2.up * 0.08f;
            ProjectileManager.Instance.SpawnProjectile(
                Type,
                muzzlePos,
                target,
                shotDamage,
                projSpeed,
                splashRadius,
                slowFactor,
                isEmpowered);
        }

        // --- IResourceConsumer ---

        public bool CanAcceptResource(ResourceType resource)
        {
            if (!IsAlive || IsGenerator || Definition == null) return false;
            if (resource != Definition.powerOrAmmoResource) return false;

            float buffered = VeinNetworkSystem.Instance != null
                ? VeinNetworkSystem.Instance.GetNodeBufferedAmount(GridPos, resource)
                : 0f;
            return buffered < 10f;
        }

        public float GetResourceNeedWeight(NetworkPriorityMode priorityMode)
        {
            if (IsGenerator || !IsAlive) return 0f;

            switch (priorityMode)
            {
                case NetworkPriorityMode.PrioritizeTurrets:
                    return 4.3f;
                case NetworkPriorityMode.Balanced:
                    return 1.6f;
                default:
                    return 0.65f;
            }
        }

        public float ReceiveResource(ResourceType resource, float amount)
        {
            if (IsGenerator || amount <= 0f) return 0f;

            _ammoFedGlowTimer = 1.5f;
            // Return 0f so VeinNetworkSystem stages the full amount directly into its node buffer
            // where ConsumeTurretShotAndGetMultiplier and GetNodeBufferedAmount read it!
            return 0f;
        }
    }
}
