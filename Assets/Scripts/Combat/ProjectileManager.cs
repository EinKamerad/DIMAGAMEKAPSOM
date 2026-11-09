using System.Collections.Generic;
using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;

namespace BioFarme.Combat
{
    /// <summary>
    /// Pooled projectile and acid/plasma impact VFX manager driven by GameManager.Instance.SimulationDeltaTime.
    /// Handles Proj_AcidGlob (Flytrap), Proj_AcidSplash (SporeFlower mortar with AoE + slow),
    /// Proj_BileSlug (BileCannon heavy AoE), and expanding shockwave/mend rings.
    /// </summary>
    public sealed class ProjectileManager : MonoBehaviour
    {
        private const int InitialProjectilePool = 40;
        private const int MaxActiveProjectiles = 160;
        private const int InitialVfxPool = 28;
        private const int MaxActiveVfx = 96;
        private const float DefaultSlowDurationSec = 2.5f;

        private sealed class PooledProjectile
        {
            public GameObject GameObject;
            public Transform Transform;
            public SpriteRenderer Renderer;
            public SpriteRenderer GlowRenderer;

            public StructureType SourceTurretType;
            public IDamageable Target;
            public Vector2 StartPos;
            public Vector2 TargetPos;
            public float Damage;
            public float Speed;
            public float SplashRadius;
            public float SlowFactor;
            public bool IsEmpowered;
            public bool IsLobbedMortar;
            public float FlightDistance;
            public float ElapsedTime;
            public float MaxLifetime;
            public bool Active;
        }

        private sealed class PooledSplashVfx
        {
            public GameObject GameObject;
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Color BaseColor;
            public float StartScale;
            public float EndScale;
            public float Elapsed;
            public float Duration;
            public bool Active;
        }

        private static ProjectileManager _instance;
        private static bool _applicationQuitting;

        public static ProjectileManager InstanceIfExists => _instance;

        public static ProjectileManager Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<ProjectileManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[ProjectileManager]");
                        _instance = go.AddComponent<ProjectileManager>();
                    }
                }
                return _instance;
            }
        }

        private Transform _projectilesRoot;
        private Transform _vfxRoot;

        private readonly Queue<PooledProjectile> _projectilePool = new Queue<PooledProjectile>(InitialProjectilePool);
        private readonly List<PooledProjectile> _activeProjectiles = new List<PooledProjectile>(InitialProjectilePool);

        private readonly Queue<PooledSplashVfx> _vfxPool = new Queue<PooledSplashVfx>(InitialVfxPool);
        private readonly List<PooledSplashVfx> _activeVfx = new List<PooledSplashVfx>(InitialVfxPool);

        private readonly List<IDamageable> _aoeHitBuffer = new List<IDamageable>(32);

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            GameObject projRootGo = new GameObject("Projectiles");
            projRootGo.transform.SetParent(transform, false);
            _projectilesRoot = projRootGo.transform;

            GameObject vfxRootGo = new GameObject("ImpactVfx");
            vfxRootGo.transform.SetParent(transform, false);
            _vfxRoot = vfxRootGo.transform;

            PrewarmPools();
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

        private void PrewarmPools()
        {
            for (int i = 0; i < InitialProjectilePool; i++)
            {
                PooledProjectile p = CreateProjectileItem();
                p.GameObject.SetActive(false);
                _projectilePool.Enqueue(p);
            }

            for (int i = 0; i < InitialVfxPool; i++)
            {
                PooledSplashVfx v = CreateVfxItem();
                v.GameObject.SetActive(false);
                _vfxPool.Enqueue(v);
            }
        }

        private void Update()
        {
            float simDt = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.SimulationDeltaTime
                : Time.deltaTime;

            if (simDt <= 0f) return;

            TickProjectiles(simDt);
            TickVfx(simDt);
        }

        public void SpawnProjectile(
            StructureType turretType,
            Vector2 startWorldPos,
            IDamageable target,
            float damage,
            float speed,
            float splashRadius,
            float slowFactor,
            bool isEmpowered)
        {
            PooledProjectile proj = AcquireProjectile();
            if (proj == null) return;

            Vector2 dest = (target != null && target.IsAlive) ? target.WorldPosition : (startWorldPos + Vector2.right * 2f);

            proj.SourceTurretType = turretType;
            proj.Target = target;
            proj.StartPos = startWorldPos;
            proj.TargetPos = dest;
            proj.Damage = Mathf.Max(1f, damage);
            proj.Speed = Mathf.Max(2.5f, speed);
            proj.SplashRadius = Mathf.Max(0f, splashRadius);
            proj.SlowFactor = Mathf.Clamp(slowFactor, 0f, 0.85f);
            proj.IsEmpowered = isEmpowered;
            proj.IsLobbedMortar = turretType == StructureType.SporeFlowerTurret;
            proj.FlightDistance = Mathf.Max(0.25f, Vector2.Distance(startWorldPos, dest));
            proj.ElapsedTime = 0f;
            proj.MaxLifetime = 3.5f;
            proj.Active = true;

            proj.Renderer.sprite = ResolveProjectileSprite(turretType);
            proj.Renderer.color = isEmpowered ? new Color(1f, 1f, 0.88f, 1f) : Color.white;

            float baseScale = turretType == StructureType.BileCannonTurret ? 0.92f : 0.76f;
            if (isEmpowered) baseScale *= 1.24f;
            proj.Transform.localScale = Vector3.one * baseScale;
            proj.Transform.position = startWorldPos;

            Vector2 initialDir = dest - startWorldPos;
            if (initialDir.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(initialDir.y, initialDir.x) * Mathf.Rad2Deg;
                proj.Transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }

            if (proj.GlowRenderer != null)
            {
                proj.GlowRenderer.gameObject.SetActive(isEmpowered);
                if (isEmpowered)
                {
                    Color glowCol = GetProjectileThemeColor(turretType);
                    glowCol.a = 0.55f;
                    proj.GlowRenderer.color = glowCol;
                }
            }

            proj.GameObject.SetActive(true);
            _activeProjectiles.Add(proj);
        }

        private static Sprite ResolveProjectileSprite(StructureType turretType)
        {
            switch (turretType)
            {
                case StructureType.FlytrapTurret:
                    return SpriteRegistry.Get("Proj_AcidGlob");
                case StructureType.SporeFlowerTurret:
                    return SpriteRegistry.Get("Proj_AcidSplash");
                case StructureType.BileCannonTurret:
                    return SpriteRegistry.Get("Proj_BileSlug");
                default:
                    return SpriteRegistry.GetProjectileSprite(turretType);
            }
        }

        private static Color GetProjectileThemeColor(StructureType turretType)
        {
            switch (turretType)
            {
                case StructureType.SporeFlowerTurret:
                    return new Color(0.75f, 0.96f, 0.28f, 1f);
                case StructureType.BileCannonTurret:
                    return new Color(1.0f, 0.48f, 0.25f, 1f);
                default:
                    return new Color(0.38f, 1.0f, 0.42f, 1f);
            }
        }

        private void TickProjectiles(float simDt)
        {
            for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
            {
                PooledProjectile p = _activeProjectiles[i];
                if (!p.Active)
                {
                    ReleaseProjectile(p);
                    _activeProjectiles.RemoveAt(i);
                    continue;
                }

                p.ElapsedTime += simDt;
                if (p.ElapsedTime >= p.MaxLifetime)
                {
                    DetonateProjectile(p, p.Transform.position);
                    ReleaseProjectile(p);
                    _activeProjectiles.RemoveAt(i);
                    continue;
                }

                if (p.Target != null && p.Target.IsAlive)
                {
                    p.TargetPos = p.Target.WorldPosition;
                }

                Vector2 currentPos = p.Transform.position;
                Vector2 toTarget = p.TargetPos - currentPos;
                float dist = toTarget.magnitude;
                float step = p.Speed * simDt;

                if (dist <= Mathf.Max(0.24f, step))
                {
                    DetonateProjectile(p, p.TargetPos);
                    ReleaseProjectile(p);
                    _activeProjectiles.RemoveAt(i);
                    continue;
                }

                Vector2 dir = toTarget / dist;
                Vector2 nextPos = currentPos + dir * step;

                if (p.IsLobbedMortar)
                {
                    float traveled = Vector2.Distance(p.StartPos, nextPos);
                    float t01 = Mathf.Clamp01(traveled / Mathf.Max(0.5f, p.FlightDistance));
                    float arcLift = Mathf.Sin(t01 * Mathf.PI) * 0.18f * simDt * 8f;
                    nextPos.y += (t01 < 0.5f ? arcLift : -arcLift * 0.5f);
                }

                p.Transform.position = nextPos;
                float rotDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                p.Transform.rotation = Quaternion.Euler(0f, 0f, rotDeg);
            }
        }

        private void DetonateProjectile(PooledProjectile p, Vector2 impactPos)
        {
            if (p.SplashRadius > 0.05f)
            {
                // AoE Splash Detonation (SporeFlowerTurret / BileCannonTurret)
                int count = WorldEntityRegistry.GetEnemiesInRadius(impactPos, p.SplashRadius, _aoeHitBuffer);
                for (int i = 0; i < count; i++)
                {
                    IDamageable enemy = _aoeHitBuffer[i];
                    if (enemy == null || !enemy.IsAlive) continue;

                    float dist = Vector2.Distance(impactPos, enemy.WorldPosition);
                    float falloff = ReferenceEquals(enemy, p.Target)
                        ? 1.0f
                        : Mathf.Lerp(1.0f, 0.65f, Mathf.Clamp01(dist / p.SplashRadius));

                    if (p.SlowFactor > 0.01f && enemy is EnemyAgent slowTarget)
                    {
                        slowTarget.ApplySlow(p.SlowFactor, DefaultSlowDurationSec);
                    }

                    enemy.TakeDamage(p.Damage * falloff, impactPos);
                }

                SpawnAcidSplashVfx(impactPos, p.SplashRadius, p.SourceTurretType, p.IsEmpowered);
            }
            else
            {
                // Single-Target Impact (FlytrapTurret)
                IDamageable victim = (p.Target != null && p.Target.IsAlive)
                    ? p.Target
                    : WorldEntityRegistry.GetClosestEnemy(impactPos, 0.55f);

                if (victim != null && victim.IsAlive)
                {
                    if (p.SlowFactor > 0.01f && victim is EnemyAgent slowTarget)
                    {
                        slowTarget.ApplySlow(p.SlowFactor, DefaultSlowDurationSec);
                    }
                    victim.TakeDamage(p.Damage, p.StartPos);
                }

                SpawnAcidSplashVfx(impactPos, 0.48f, p.SourceTurretType, p.IsEmpowered);
            }
        }

        private void SpawnAcidSplashVfx(Vector2 pos, float radius, StructureType turretType, bool empowered)
        {
            PooledSplashVfx vfx = AcquireVfx();
            if (vfx == null) return;

            Color col = GetProjectileThemeColor(turretType);
            col.a = empowered ? 0.88f : 0.72f;

            vfx.Renderer.sprite = radius >= 0.9f
                ? SpriteRegistry.GetSoftCircleSprite()
                : SpriteRegistry.Get("VFX_AcidSpray_01");
            vfx.BaseColor = col;
            vfx.Renderer.color = col;
            vfx.StartScale = Mathf.Max(0.25f, radius * 0.45f);
            vfx.EndScale = Mathf.Max(0.55f, radius * (radius >= 0.9f ? 2.0f : 0.85f));
            vfx.Elapsed = 0f;
            vfx.Duration = radius >= 0.9f ? 0.28f : 0.18f;
            vfx.Active = true;

            vfx.Transform.position = pos;
            vfx.Transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            vfx.Transform.localScale = Vector3.one * vfx.StartScale;
            vfx.GameObject.SetActive(true);

            _activeVfx.Add(vfx);
        }

        /// <summary>
        /// Spawns a clean expanding biopunk pulse ring VFX (used by Core Defib shockwave, MendTendril, BioReactor, and enemy deaths).
        /// </summary>
        public void SpawnWorldRingVfx(Vector2 worldPos, float targetRadius, Color color, float duration = 0.30f)
        {
            PooledSplashVfx vfx = AcquireVfx();
            if (vfx == null) return;

            vfx.Renderer.sprite = SpriteRegistry.GetSoftCircleSprite();
            vfx.BaseColor = color;
            vfx.Renderer.color = color;
            vfx.StartScale = Mathf.Max(0.20f, targetRadius * 0.22f);
            vfx.EndScale = Mathf.Max(0.45f, targetRadius * 2.0f);
            vfx.Elapsed = 0f;
            vfx.Duration = Mathf.Max(0.10f, duration);
            vfx.Active = true;

            vfx.Transform.position = worldPos;
            vfx.Transform.rotation = Quaternion.identity;
            vfx.Transform.localScale = Vector3.one * vfx.StartScale;
            vfx.GameObject.SetActive(true);

            _activeVfx.Add(vfx);
        }

        private void TickVfx(float simDt)
        {
            for (int i = _activeVfx.Count - 1; i >= 0; i--)
            {
                PooledSplashVfx v = _activeVfx[i];
                if (!v.Active)
                {
                    ReleaseVfx(v);
                    _activeVfx.RemoveAt(i);
                    continue;
                }

                v.Elapsed += simDt;
                float t = Mathf.Clamp01(v.Elapsed / v.Duration);
                float eased = 1f - (1f - t) * (1f - t);

                float scale = Mathf.Lerp(v.StartScale, v.EndScale, eased);
                v.Transform.localScale = new Vector3(scale, scale, 1f);

                Color c = v.BaseColor;
                c.a = v.BaseColor.a * (1f - t);
                v.Renderer.color = c;

                if (v.Elapsed >= v.Duration)
                {
                    ReleaseVfx(v);
                    _activeVfx.RemoveAt(i);
                }
            }
        }

        public void ClearAllProjectiles()
        {
            for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
            {
                ReleaseProjectile(_activeProjectiles[i]);
            }
            _activeProjectiles.Clear();

            for (int i = _activeVfx.Count - 1; i >= 0; i--)
            {
                ReleaseVfx(_activeVfx[i]);
            }
            _activeVfx.Clear();
        }

        private PooledProjectile AcquireProjectile()
        {
            if (_projectilePool.Count > 0)
            {
                return _projectilePool.Dequeue();
            }
            if (_activeProjectiles.Count >= MaxActiveProjectiles)
            {
                return null;
            }
            return CreateProjectileItem();
        }

        private void ReleaseProjectile(PooledProjectile p)
        {
            if (p == null) return;
            p.Active = false;
            p.Target = null;
            if (p.GameObject != null)
            {
                p.GameObject.SetActive(false);
            }
            _projectilePool.Enqueue(p);
        }

        private PooledProjectile CreateProjectileItem()
        {
            GameObject go = new GameObject("BioProjectile");
            go.transform.SetParent(_projectilesRoot, false);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 42;

            GameObject glowGo = new GameObject("EmpoweredHalo");
            glowGo.transform.SetParent(go.transform, false);
            glowGo.transform.localScale = Vector3.one * 1.45f;
            SpriteRenderer glowSr = glowGo.AddComponent<SpriteRenderer>();
            glowSr.sprite = SpriteRegistry.GetSoftCircleSprite();
            glowSr.sortingOrder = 41;
            glowGo.SetActive(false);

            return new PooledProjectile
            {
                GameObject = go,
                Transform = go.transform,
                Renderer = sr,
                GlowRenderer = glowSr,
                Active = false
            };
        }

        private PooledSplashVfx AcquireVfx()
        {
            if (_vfxPool.Count > 0)
            {
                return _vfxPool.Dequeue();
            }
            if (_activeVfx.Count >= MaxActiveVfx)
            {
                return null;
            }
            return CreateVfxItem();
        }

        private void ReleaseVfx(PooledSplashVfx v)
        {
            if (v == null) return;
            v.Active = false;
            if (v.GameObject != null)
            {
                v.GameObject.SetActive(false);
            }
            _vfxPool.Enqueue(v);
        }

        private PooledSplashVfx CreateVfxItem()
        {
            GameObject go = new GameObject("SplashVfx");
            go.transform.SetParent(_vfxRoot, false);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 40;
            sr.sprite = SpriteRegistry.GetSoftCircleSprite();

            return new PooledSplashVfx
            {
                GameObject = go,
                Transform = go.transform,
                Renderer = sr,
                Active = false
            };
        }
    }
}
