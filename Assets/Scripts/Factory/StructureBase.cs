using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;
using BioFarme.Audio;
using BioFarme.Combat;

namespace BioFarme.Factory
{
    /// <summary>
    /// Base world structure entity implementing IGridEntity and IDamageable.
    /// Provides hit-flash, placement spring animation, compact world-space health bar when injured,
    /// repair handling, and clean grid unregistration on destruction.
    /// </summary>
    public abstract class StructureBase : MonoBehaviour, IGridEntity, IDamageable
    {
        public Vector2Int GridPos { get; protected set; }
        public StructureType Type { get; protected set; }
        public StructureDefinition Definition { get; protected set; }
        public bool IsStarterStructure { get; protected set; }

        public float CurrentHp { get; protected set; }
        public float MaxHp { get; protected set; }
        public Vector2 WorldPosition => transform.position;
        public bool IsAlive => !_isDestroyed && CurrentHp > 0f;

        protected SpriteRenderer MainRenderer { get; private set; }
        protected Transform VisualRoot { get; private set; }

        private GameObject _hpBarRoot;
        private Transform _hpBarFillTransform;
        private SpriteRenderer _hpBarFillRenderer;
        private const float BarWidth = 0.76f;
        private const float BarHeight = 0.09f;

        private float _spawnPopProgress = 1f;
        private float _hitFlashTimer;
        private float _healFlashTimer;
        protected Color BaseTintColor = Color.white;
        protected Vector3 RecoilScale = Vector3.one;
        protected Vector3 AnimLocalOffset = Vector3.zero;
        private bool _isDestroyed;

        public virtual void Initialize(Vector2Int anchorPos, StructureType type, StructureDefinition def, bool isStarter = false)
        {
            GridPos = anchorPos;
            Type = type;
            Definition = def ?? BalanceDatabase.GetStructure(type);
            IsStarterStructure = isStarter;
            _isDestroyed = false;

            MaxHp = Definition != null && Definition.maxHp > 0f ? Definition.maxHp : 150f;
            CurrentHp = MaxHp;
            _spawnPopProgress = isStarter ? 1f : 0f;

            SetupRendererHierarchy();
            OnInitialized();
            UpdateHealthBar();
        }

        private void SetupRendererHierarchy()
        {
            GameObject visualGo = new GameObject("Visual");
            visualGo.transform.SetParent(transform, false);
            visualGo.transform.localPosition = Vector3.zero;
            VisualRoot = visualGo.transform;

            // Keep SpriteRenderer on root or VisualRoot?
            // Note: BioGridManager.RefreshAutoTilesAroundFootprint checks GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>()!
            MainRenderer = visualGo.AddComponent<SpriteRenderer>();
            MainRenderer.sprite = SpriteRegistry.GetStructureSprite(Type);
            MainRenderer.sortingOrder = GetDesiredSortingOrder();
        }

        protected virtual int GetDesiredSortingOrder()
        {
            if (Definition != null && Definition.isConduit)
            {
                return -48;
            }
            // Subtle Y-sorting so lower rows layer naturally in front of upper rows
            return 10 + Mathf.Clamp(BioGridManager.Height - GridPos.y, 0, 30);
        }

        protected virtual void OnInitialized() { }

        protected virtual void Update()
        {
            if (_isDestroyed) return;

            float visualDt = Time.deltaTime;
            float simDt = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.SimulationDeltaTime
                : Time.deltaTime;

            TickBaseVisuals(visualDt);

            if (simDt > 0f)
            {
                OnSimulationTick(simDt);
            }
        }

        protected virtual void OnSimulationTick(float simDt) { }

        private void TickBaseVisuals(float dt)
        {
            if (VisualRoot == null) return;

            float popScale = 1f;
            if (_spawnPopProgress < 1f)
            {
                _spawnPopProgress = Mathf.Min(1f, _spawnPopProgress + dt * 5.2f);
                if (_spawnPopProgress < 0.65f)
                {
                    popScale = Mathf.Lerp(0.45f, 1.12f, _spawnPopProgress / 0.65f);
                }
                else
                {
                    popScale = Mathf.Lerp(1.12f, 1.0f, (_spawnPopProgress - 0.65f) / 0.35f);
                }
            }

            RecoilScale = Vector3.Lerp(RecoilScale, Vector3.one, Mathf.Clamp01(dt * 12f));
            VisualRoot.localScale = new Vector3(RecoilScale.x * popScale, RecoilScale.y * popScale, 1f);
            VisualRoot.localPosition = AnimLocalOffset;

            if (MainRenderer != null)
            {
                if (_hitFlashTimer > 0f)
                {
                    _hitFlashTimer = Mathf.Max(0f, _hitFlashTimer - dt);
                    MainRenderer.color = new Color(1f, 0.58f, 0.58f, 1f);
                }
                else if (_healFlashTimer > 0f)
                {
                    _healFlashTimer = Mathf.Max(0f, _healFlashTimer - dt);
                    MainRenderer.color = new Color(0.65f, 1f, 0.88f, 1f);
                }
                else
                {
                    MainRenderer.color = BaseTintColor;
                }
            }
        }

        public virtual void RefreshNeighbors(bool propagateToCardinals = false) { }

        public virtual void TakeDamage(float amount, Vector2 hitSource)
        {
            if (!IsAlive || amount <= 0f) return;

            CurrentHp = Mathf.Max(0f, CurrentHp - amount);
            _hitFlashTimer = 0.11f;
            RecoilScale = new Vector3(1.12f, 0.88f, 1f);

            WorldPopupManager.SpawnDamage(WorldPosition, amount, false);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxEnemyHit, WorldPosition, 0.55f, 0.08f);
            }

            UpdateHealthBar();
            OnDamaged(amount, hitSource);

            if (CurrentHp <= 0f)
            {
                DestroyFromDamage();
            }
        }

        protected virtual void OnDamaged(float amount, Vector2 hitSource) { }

        public virtual void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f || CurrentHp >= MaxHp) return;

            float prev = CurrentHp;
            CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
            float gained = CurrentHp - prev;

            if (gained >= 1f)
            {
                _healFlashTimer = 0.18f;
                RecoilScale = new Vector3(0.94f, 1.08f, 1f);
                WorldPopupManager.SpawnHeal(WorldPosition + Vector2.up * 0.25f, gained);
            }

            UpdateHealthBar();
        }

        protected virtual void DestroyFromDamage()
        {
            if (_isDestroyed) return;
            _isDestroyed = true;

            Vector2 pos = WorldPosition;
            Vector2Int cell = GridPos;

            if (BioGridManager.InstanceIfExists != null)
            {
                BioGridManager.InstanceIfExists.UnregisterStructure(cell);
            }

            if (StructureManager.InstanceIfExists != null)
            {
                StructureManager.InstanceIfExists.NotifyStructureRemovedFromWorld(this, cell);
            }

            if (ProjectileManager.InstanceIfExists != null)
            {
                ProjectileManager.InstanceIfExists.SpawnWorldRingVfx(pos, 0.75f, new Color(0.85f, 0.35f, 0.28f, 0.8f), 0.25f);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxDemolish, pos, 0.65f, 0.08f);
            }

            Destroy(gameObject);
        }

        public void MarkDemolishedCleanly()
        {
            _isDestroyed = true;
        }

        private void EnsureHealthBarCreated()
        {
            if (_hpBarRoot != null) return;

            _hpBarRoot = new GameObject("StructureHpBar");
            _hpBarRoot.transform.SetParent(transform, false);
            _hpBarRoot.transform.localPosition = new Vector3(0f, 0.56f, 0f);

            Sprite whiteSq = SpriteRegistry.GetWhiteSquareSprite();

            GameObject bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(_hpBarRoot.transform, false);
            bgGo.transform.localScale = new Vector3(BarWidth + 0.04f, BarHeight + 0.04f, 1f);
            SpriteRenderer bgSr = bgGo.AddComponent<SpriteRenderer>();
            bgSr.sprite = whiteSq;
            bgSr.color = new Color(0.06f, 0.03f, 0.08f, 0.88f);
            bgSr.sortingOrder = 55;

            GameObject fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(_hpBarRoot.transform, false);
            _hpBarFillTransform = fillGo.transform;
            _hpBarFillRenderer = fillGo.AddComponent<SpriteRenderer>();
            _hpBarFillRenderer.sprite = whiteSq;
            _hpBarFillRenderer.sortingOrder = 56;
        }

        protected void UpdateHealthBar()
        {
            bool injured = CurrentHp < MaxHp - 0.5f && CurrentHp > 0f;
            if (!injured)
            {
                if (_hpBarRoot != null) _hpBarRoot.SetActive(false);
                return;
            }

            EnsureHealthBarCreated();
            _hpBarRoot.SetActive(true);

            float ratio = Mathf.Clamp01(CurrentHp / Mathf.Max(1f, MaxHp));
            float fillW = BarWidth * ratio;
            _hpBarFillTransform.localScale = new Vector3(fillW, BarHeight, 1f);
            _hpBarFillTransform.localPosition = new Vector3(-(BarWidth - fillW) * 0.5f, 0f, 0f);

            if (_hpBarFillRenderer != null)
            {
                _hpBarFillRenderer.color = ratio > 0.5f
                    ? new Color(0.32f, 0.92f, 0.48f, 0.95f)
                    : (ratio > 0.25f ? new Color(1.0f, 0.72f, 0.22f, 0.95f) : new Color(0.98f, 0.24f, 0.24f, 0.95f));
            }
        }
    }
}
