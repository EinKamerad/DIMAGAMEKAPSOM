using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;
using BioFarme.Audio;
using BioFarme.Factory;

namespace BioFarme.Combat
{
    /// <summary>
    /// Hostile mutant hive organism (SwarmMite, MantisBug, CarapaceCrab, BroodAlphaBoss).
    /// Follows weighted FlowFieldPathfinder toward the 3x3 Living Heart Core, smoothly avoids fellow swarm bugs,
    /// lunges at blocking BoneWalls / Turrets / Core, displays a crisp pixel-art health bar, flashes white on hit,
    /// spawns floating damage numbers, and drops Biomass / Spores / BoneChitin / Mutagen on death!
    /// </summary>
    public sealed class EnemyAgent : MonoBehaviour, IDamageable
    {
        public EnemyType Type { get; private set; }
        public EnemyDefinition Definition { get; private set; }

        public float CurrentHp { get; private set; }
        public float MaxHp { get; private set; }
        public Vector2 WorldPosition => transform.position;
        public bool IsAlive => !_isDead && CurrentHp > 0f;

        private Transform _visualTransform;
        private SpriteRenderer _spriteRenderer;
        private SpriteRenderer _bossShadowGlow;

        // World-space pixel-art health bar
        private GameObject _hpBarRoot;
        private Transform _hpBarFillTransform;
        private SpriteRenderer _hpBarFillRenderer;
        private SpriteRenderer _hpBarBgRenderer;
        private float _hpBarWidth = 0.72f;
        private float _hpBarHeight = 0.09f;

        private float _baseVisualScale = 1f;
        private float _walkAnimTimer;
        private int _walkFrameIndex;
        private float _scuttleBobPhase;

        private float _attackCooldown;
        private Vector2 _lungeOffset;
        private float _hitFlashTimer;
        private float _slowTimer;
        private float _slowFactor;
        private Vector2 _knockbackVelocity;
        private float _cropNibbleCooldown;
        private bool _isDead;

        public void Initialize(EnemyType enemyType, Vector2 spawnWorldPos)
        {
            Type = enemyType;
            Definition = BalanceDatabase.GetEnemy(enemyType);
            _isDead = false;

            MaxHp = Definition != null && Definition.maxHp > 0f ? Definition.maxHp : 60f;
            CurrentHp = MaxHp;
            _baseVisualScale = Definition != null && Definition.visualScale > 0.1f ? Definition.visualScale : 1f;
            _scuttleBobPhase = Random.Range(0f, Mathf.PI * 2f);
            _attackCooldown = 0.15f;
            _slowTimer = 0f;
            _slowFactor = 0f;
            _knockbackVelocity = Vector2.zero;

            transform.position = new Vector3(spawnWorldPos.x, spawnWorldPos.y, 0f);
            gameObject.name = $"Enemy_{enemyType}";

            BuildVisuals();
            UpdateHealthBarVisual();

            WorldEntityRegistry.RegisterEnemy(this);
            GameEvents.RaiseEnemySpawned(Type, spawnWorldPos);
        }

        private void BuildVisuals()
        {
            GameObject visGo = new GameObject("SpriteVisual");
            visGo.transform.SetParent(transform, false);
            _visualTransform = visGo.transform;
            _visualTransform.localScale = Vector3.one * _baseVisualScale;

            _spriteRenderer = visGo.AddComponent<SpriteRenderer>();
            _spriteRenderer.sprite = SpriteRegistry.GetEnemyFrameSprite(Type, 0);
            _spriteRenderer.sortingOrder = (Definition != null && Definition.isBoss) ? 34 : 28;

            if (Definition != null && Definition.isBoss)
            {
                GameObject auraGo = new GameObject("BossMenaceAura");
                auraGo.transform.SetParent(transform, false);
                auraGo.transform.localScale = Vector3.one * 2.8f;
                _bossShadowGlow = auraGo.AddComponent<SpriteRenderer>();
                _bossShadowGlow.sprite = SpriteRegistry.GetSoftCircleSprite();
                _bossShadowGlow.color = new Color(0.75f, 0.16f, 0.62f, 0.32f);
                _bossShadowGlow.sortingOrder = 25;
            }

            // Crisp pixel-art red/dark health bar above enemy
            bool isBoss = Definition != null && Definition.isBoss;
            _hpBarWidth = isBoss ? 1.75f : (Type == EnemyType.CarapaceCrab ? 0.92f : 0.68f);
            _hpBarHeight = isBoss ? 0.16f : 0.085f;
            float yOffset = isBoss ? 1.28f : (0.55f * _baseVisualScale + 0.14f);

            _hpBarRoot = new GameObject("EnemyHpBar");
            _hpBarRoot.transform.SetParent(transform, false);
            _hpBarRoot.transform.localPosition = new Vector3(0f, yOffset, 0f);

            Sprite whitePixel = SpriteRegistry.GetWhiteSquareSprite();

            GameObject borderGo = new GameObject("Border");
            borderGo.transform.SetParent(_hpBarRoot.transform, false);
            borderGo.transform.localScale = new Vector3(_hpBarWidth + 0.05f, _hpBarHeight + 0.05f, 1f);
            SpriteRenderer borderSr = borderGo.AddComponent<SpriteRenderer>();
            borderSr.sprite = whitePixel;
            borderSr.color = isBoss
                ? new Color(0.95f, 0.78f, 0.25f, 0.95f)
                : new Color(0.05f, 0.02f, 0.06f, 0.92f);
            borderSr.sortingOrder = 57;

            GameObject bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(_hpBarRoot.transform, false);
            bgGo.transform.localScale = new Vector3(_hpBarWidth, _hpBarHeight, 1f);
            _hpBarBgRenderer = bgGo.AddComponent<SpriteRenderer>();
            _hpBarBgRenderer.sprite = whitePixel;
            _hpBarBgRenderer.color = new Color(0.16f, 0.05f, 0.07f, 0.90f);
            _hpBarBgRenderer.sortingOrder = 58;

            GameObject fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(_hpBarRoot.transform, false);
            _hpBarFillTransform = fillGo.transform;
            _hpBarFillRenderer = fillGo.AddComponent<SpriteRenderer>();
            _hpBarFillRenderer.sprite = whitePixel;
            _hpBarFillRenderer.color = isBoss
                ? new Color(0.98f, 0.22f, 0.52f, 0.98f)
                : new Color(0.96f, 0.22f, 0.20f, 0.96f);
            _hpBarFillRenderer.sortingOrder = 59;
        }

        private void OnEnable()
        {
            if (!_isDead && Definition != null)
            {
                WorldEntityRegistry.RegisterEnemy(this);
            }
        }

        private void OnDisable()
        {
            WorldEntityRegistry.UnregisterEnemy(this);
        }

        private void OnDestroy()
        {
            WorldEntityRegistry.UnregisterEnemy(this);
        }

        private void Update()
        {
            if (!IsAlive) return;

            float simDt = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.SimulationDeltaTime
                : Time.deltaTime;

            if (simDt <= 0f) return;

            TickTimersAndDebuffs(simDt);

            IDamageable attackTarget = FindImmediateAttackTarget();
            if (attackTarget != null && attackTarget.IsAlive)
            {
                PerformAttackBehavior(attackTarget, simDt);
            }
            else
            {
                PerformFlowMovement(simDt);
            }

            ApplyKnockbackDisplacement(simDt);
            UpdateVisualPose(simDt);
        }

        private void TickTimersAndDebuffs(float simDt)
        {
            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer = Mathf.Max(0f, _hitFlashTimer - simDt);
            }

            if (_slowTimer > 0f)
            {
                _slowTimer = Mathf.Max(0f, _slowTimer - simDt);
                if (_slowTimer <= 0f)
                {
                    _slowFactor = 0f;
                }
            }

            if (_attackCooldown > 0f)
            {
                _attackCooldown -= simDt;
            }

            if (_cropNibbleCooldown > 0f)
            {
                _cropNibbleCooldown -= simDt;
            }
        }

        private IDamageable FindImmediateAttackTarget()
        {
            float atkRange = Definition != null ? Mathf.Max(0.75f, Definition.attackRange) : 0.85f;

            // 1. Check proximity to the 3x3 Living Heart Core (world footprint [17..20, 11..14])
            CoreHeartEntity heart = CoreHeartEntity.InstanceIfExists;
            if (heart != null && heart.IsAlive)
            {
                Vector2 coreClosest = new Vector2(
                    Mathf.Clamp(WorldPosition.x, 17.0f, 20.0f),
                    Mathf.Clamp(WorldPosition.y, 11.0f, 14.0f));
                if (Vector2.Distance(WorldPosition, coreClosest) <= atkRange * 0.85f)
                {
                    return heart;
                }
            }

            // 2. Check FlowFieldPathfinder for blocking BoneWall / Fence / Turret / Factory directly ahead
            FlowFieldPathfinder pathfinder = FlowFieldPathfinder.Instance;
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (pathfinder != null && grid != null)
            {
                IGridEntity blocker = pathfinder.GetBlockingStructureAhead(WorldPosition, 0.70f);
                if (blocker != null)
                {
                    IDamageable blockerDmg = blocker as IDamageable ?? grid.GetDamageableAt(blocker.GridPos);
                    if (blockerDmg != null && blockerDmg.IsAlive)
                    {
                        float distToBlocker = Vector2.Distance(WorldPosition, grid.GridToWorldCenter(blocker.GridPos));
                        if (distToBlocker <= atkRange + 0.55f || blocker.Type == StructureType.LivingHeartCore)
                        {
                            return blockerDmg;
                        }
                    }
                }
            }

            // 3. SwarmMite opportunistic nibble on adjacent non-conduit structures or crops
            if (Type == EnemyType.SwarmMite && grid != null)
            {
                Vector2Int myCell = grid.WorldToGrid(WorldPosition);
                TryOpportunisticCropNibble(grid, myCell);
            }

            return null;
        }

        private void TryOpportunisticCropNibble(BioGridManager grid, Vector2Int myCell)
        {
            if (_cropNibbleCooldown > 0f) return;

            ICropEntity crop = WorldEntityRegistry.GetCropAt(myCell);
            if (crop != null)
            {
                _cropNibbleCooldown = 3.2f;
                _lungeOffset = Vector2.down * 0.10f;
                WorldPopupManager.SpawnText(
                    WorldPosition + Vector2.up * 0.25f,
                    "Хрум!",
                    new Color(0.95f, 0.76f, 0.30f, 1f),
                    0.78f,
                    false);
            }
        }

        private void PerformAttackBehavior(IDamageable target, float simDt)
        {
            Vector2 toTarget = target.WorldPosition - WorldPosition;
            UpdateHorizontalFacing(toTarget.x);

            // Subtle agitated mandible twitch while waiting for swing
            _walkAnimTimer += simDt * 6.5f;
            if (_walkAnimTimer >= 1f)
            {
                _walkAnimTimer -= 1f;
                _walkFrameIndex++;
                if (_spriteRenderer != null)
                {
                    _spriteRenderer.sprite = SpriteRegistry.GetEnemyFrameSprite(Type, _walkFrameIndex);
                }
            }

            if (_attackCooldown > 0f) return;

            float interval = Definition != null && Definition.attackInterval > 0.15f ? Definition.attackInterval : 1.0f;
            float slowPenalty = _slowTimer > 0f ? (1f + _slowFactor * 0.35f) : 1f;
            _attackCooldown = interval * slowPenalty;

            float dmg = Definition != null && Definition.attackDamage > 0f ? Definition.attackDamage : 10f;
            Vector2 dir = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : Vector2.right;

            // Lunge forward animation!
            _lungeOffset = dir * (Definition != null && Definition.isBoss ? 0.32f : 0.22f);

            // Thorny OrganicFence slows down biting insects!
            if (target is BoneWallEntity wall && wall.ThornsSlowFactor > 0.05f)
            {
                ApplySlow(wall.ThornsSlowFactor, 1.8f);
            }

            target.TakeDamage(dmg, WorldPosition);
        }

        private void PerformFlowMovement(float simDt)
        {
            FlowFieldPathfinder pathfinder = FlowFieldPathfinder.Instance;
            BioGridManager grid = BioGridManager.InstanceIfExists;

            Vector2 flowDir = pathfinder != null
                ? pathfinder.GetFlowDirection(WorldPosition)
                : ((new Vector2(18.5f, 12.5f) - WorldPosition).normalized);

            // Soft local separation so swarms spread naturally across corridors
            Vector2 separation = ComputeSwarmSeparation();
            Vector2 moveDir = flowDir + separation * 0.42f;
            if (moveDir.sqrMagnitude > 0.0001f)
            {
                moveDir.Normalize();
            }

            float baseSpeed = Definition != null && Definition.moveSpeed > 0.1f ? Definition.moveSpeed : 1.8f;
            float weatherMult = (GameManager.InstanceIfExists != null && GameManager.InstanceIfExists.CurrentWeatherDefinition != null)
                ? GameManager.InstanceIfExists.CurrentWeatherDefinition.enemySpeedMultiplier
                : 1.0f;
            float slowMult = _slowTimer > 0f ? Mathf.Clamp(1f - _slowFactor, 0.25f, 1f) : 1.0f;

            float effectiveSpeed = baseSpeed * weatherMult * slowMult;
            Vector2 delta = moveDir * (effectiveSpeed * simDt);
            Vector2 candidatePos = WorldPosition + delta;

            if (grid != null)
            {
                candidatePos = ConstrainOutOfRockAndWalls(grid, WorldPosition, candidatePos);
            }

            transform.position = new Vector3(candidatePos.x, candidatePos.y, 0f);
            UpdateHorizontalFacing(moveDir.x);

            // Walk frame animation scaled by movement speed
            _walkAnimTimer += simDt * (5.5f + effectiveSpeed * 2.4f);
            if (_walkAnimTimer >= 1f)
            {
                _walkAnimTimer -= 1f;
                _walkFrameIndex++;
                if (_spriteRenderer != null)
                {
                    _spriteRenderer.sprite = SpriteRegistry.GetEnemyFrameSprite(Type, _walkFrameIndex);
                }
            }

            _scuttleBobPhase += simDt * (8f + effectiveSpeed * 3f);
        }

        private Vector2 ComputeSwarmSeparation()
        {
            var enemies = WorldEntityRegistry.ActiveEnemies;
            int count = enemies.Count;
            if (count <= 1) return Vector2.zero;

            Vector2 push = Vector2.zero;
            Vector2 myPos = WorldPosition;
            const float sepRadius = 0.58f;
            const float sepRadiusSq = sepRadius * sepRadius;

            int checkedCount = 0;
            for (int i = 0; i < count && checkedCount < 20; i++)
            {
                IDamageable other = enemies[i];
                if (other == null || ReferenceEquals(other, this) || !other.IsAlive) continue;

                Vector2 diff = myPos - other.WorldPosition;
                float dSq = diff.sqrMagnitude;
                if (dSq > 0.0001f && dSq < sepRadiusSq)
                {
                    float d = Mathf.Sqrt(dSq);
                    push += (diff / d) * ((sepRadius - d) / sepRadius);
                    checkedCount++;
                }
            }

            return push;
        }

        private static Vector2 ConstrainOutOfRockAndWalls(BioGridManager grid, Vector2 current, Vector2 desired)
        {
            Vector2Int destCell = grid.WorldToGrid(desired);
            if (IsCellPassableForEnemyBody(grid, destCell))
            {
                return desired;
            }

            // Try sliding cleanly along X or Y axis
            Vector2 slideX = new Vector2(desired.x, current.y);
            if (IsCellPassableForEnemyBody(grid, grid.WorldToGrid(slideX)))
            {
                return slideX;
            }

            Vector2 slideY = new Vector2(current.x, desired.y);
            if (IsCellPassableForEnemyBody(grid, grid.WorldToGrid(slideY)))
            {
                return slideY;
            }

            return current;
        }

        private static bool IsCellPassableForEnemyBody(BioGridManager grid, Vector2Int cell)
        {
            if (!grid.IsInBounds(cell)) return false;
            if (grid.GetGroundType(cell) == TileGroundType.CavernRock) return false;

            IGridEntity ent = grid.GetStructureAt(cell);
            if (ent == null) return true;

            StructureDefinition def = BalanceDatabase.GetStructure(ent.Type);
            return def == null || !def.blocksEnemies;
        }

        private void ApplyKnockbackDisplacement(float simDt)
        {
            if (_knockbackVelocity.sqrMagnitude < 0.001f) return;

            BioGridManager grid = BioGridManager.InstanceIfExists;
            Vector2 desired = WorldPosition + _knockbackVelocity * simDt;
            if (grid != null)
            {
                desired = ConstrainOutOfRockAndWalls(grid, WorldPosition, desired);
            }
            transform.position = new Vector3(desired.x, desired.y, 0f);
            _knockbackVelocity = Vector2.MoveTowards(_knockbackVelocity, Vector2.zero, 16f * simDt);
        }

        private void UpdateHorizontalFacing(float vx)
        {
            if (_spriteRenderer == null || Mathf.Abs(vx) < 0.06f) return;

            // Curated PNG orientations:
            // SwarmMite & BroodAlphaBoss face Right (+X) natively; MantisBug & CarapaceCrab face Left (-X) natively!
            bool nativeFacesLeft = (Type == EnemyType.MantisBug || Type == EnemyType.CarapaceCrab);
            _spriteRenderer.flipX = nativeFacesLeft ? (vx > 0f) : (vx < 0f);
        }

        private void UpdateVisualPose(float simDt)
        {
            if (_visualTransform == null) return;

            _lungeOffset = Vector2.Lerp(_lungeOffset, Vector2.zero, Mathf.Clamp01(simDt * 11f));
            float bobY = Mathf.Sin(_scuttleBobPhase) * 0.025f;
            _visualTransform.localPosition = new Vector3(_lungeOffset.x, _lungeOffset.y + bobY, 0f);

            float squash = 1f + (_hitFlashTimer > 0f ? 0.14f : 0f);
            _visualTransform.localScale = new Vector3(
                _baseVisualScale * squash,
                _baseVisualScale * (2f - squash),
                1f);

            if (_spriteRenderer != null)
            {
                if (_hitFlashTimer > 0f)
                {
                    _spriteRenderer.color = new Color(1f, 0.96f, 0.92f, 1f) * 1.4f;
                }
                else if (_slowTimer > 0f)
                {
                    _spriteRenderer.color = new Color(0.68f, 0.96f, 0.55f, 1f);
                }
                else
                {
                    _spriteRenderer.color = Color.white;
                }
            }

            if (_bossShadowGlow != null)
            {
                float pulse = 2.75f + Mathf.Sin(Time.time * 4.5f) * 0.25f;
                _bossShadowGlow.transform.localScale = new Vector3(pulse, pulse, 1f);
            }
        }

        // --- Debuffs & Knockback ---

        public void ApplySlow(float slowFactor, float durationSec = 2.5f)
        {
            if (slowFactor <= 0f || durationSec <= 0f) return;
            _slowFactor = Mathf.Max(_slowFactor, Mathf.Clamp(slowFactor, 0f, 0.75f));
            _slowTimer = Mathf.Max(_slowTimer, durationSec);
        }

        public void ApplyKnockback(Vector2 impulse)
        {
            float resist = (Definition != null && Definition.isBoss) ? 0.35f : (Type == EnemyType.CarapaceCrab ? 0.6f : 1.0f);
            _knockbackVelocity = Vector2.ClampMagnitude(_knockbackVelocity + impulse * resist, 9.0f);
        }

        // --- IDamageable ---

        public void TakeDamage(float amount, Vector2 hitSource)
        {
            if (!IsAlive || amount <= 0f) return;

            CurrentHp = Mathf.Max(0f, CurrentHp - amount);
            _hitFlashTimer = 0.10f;

            // Subtle impact knockback
            Vector2 away = WorldPosition - hitSource;
            if (away.sqrMagnitude > 0.001f)
            {
                ApplyKnockback(away.normalized * 0.85f);
            }

            // Floating damage numbers matching reference screenshot ("21", "255", "545")
            WorldPopupManager.SpawnDamage(WorldPosition, amount, amount >= 40f);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxEnemyHit, WorldPosition, 0.58f, 0.08f);
            }

            UpdateHealthBarVisual();

            if (CurrentHp <= 0f)
            {
                Die();
            }
        }

        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f) return;
            CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
            UpdateHealthBarVisual();
        }

        private void UpdateHealthBarVisual()
        {
            if (_hpBarRoot == null || _hpBarFillTransform == null) return;

            bool isBoss = Definition != null && Definition.isBoss;
            bool injured = CurrentHp < MaxHp - 0.1f;

            _hpBarRoot.SetActive(isBoss || injured);
            if (!_hpBarRoot.activeSelf) return;

            float ratio = Mathf.Clamp01(CurrentHp / Mathf.Max(1f, MaxHp));
            float fillW = _hpBarWidth * ratio;
            _hpBarFillTransform.localScale = new Vector3(fillW, _hpBarHeight, 1f);
            _hpBarFillTransform.localPosition = new Vector3(-(_hpBarWidth - fillW) * 0.5f, 0f, 0f);
        }

        private void Die()
        {
            if (_isDead) return;
            _isDead = true;

            Vector2 deathPos = WorldPosition;
            WorldEntityRegistry.UnregisterEnemy(this);

            if (AudioManager.Instance != null)
            {
                float vol = (Definition != null && Definition.isBoss) ? 1.0f : 0.72f;
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxEnemyDie, deathPos, vol, 0.07f);
            }

            // Award primary & bonus drops (Biomass, Spores, BoneChitin from CarapaceCrab, Mutagen from BroodAlphaBoss!)
            if (Definition != null && ResourceManager.Instance != null)
            {
                if (Definition.dropAmount > 0)
                {
                    ResourceManager.Instance.Add(Definition.dropResource, Definition.dropAmount);
                    if (Definition.dropResource == ResourceType.Mutagen ||
                        Definition.dropResource == ResourceType.BoneChitin ||
                        Definition.isBoss)
                    {
                        WorldPopupManager.SpawnResourceGain(deathPos + Vector2.up * 0.25f, Definition.dropResource, Definition.dropAmount);
                    }
                }

                if (Definition.bonusDropAmount > 0)
                {
                    ResourceManager.Instance.Add(Definition.bonusDropResource, Definition.bonusDropAmount);
                    if (Definition.bonusDropResource == ResourceType.Mutagen || Definition.isBoss)
                    {
                        WorldPopupManager.SpawnResourceGain(deathPos + Vector2.down * 0.15f, Definition.bonusDropResource, Definition.bonusDropAmount);
                    }
                }
            }

            // Acid/ichor death burst VFX
            if (ProjectileManager.InstanceIfExists != null)
            {
                bool isBoss = Definition != null && Definition.isBoss;
                Color burstCol = isBoss
                    ? new Color(0.96f, 0.28f, 0.88f, 0.90f)
                    : new Color(0.45f, 0.92f, 0.32f, 0.78f);
                ProjectileManager.InstanceIfExists.SpawnWorldRingVfx(
                    deathPos,
                    isBoss ? 2.4f : 0.68f,
                    burstCol,
                    isBoss ? 0.50f : 0.22f);
            }

            GameManager.Instance?.RecordEnemyKilled(Type, deathPos);
            Destroy(gameObject);
        }
    }
}
