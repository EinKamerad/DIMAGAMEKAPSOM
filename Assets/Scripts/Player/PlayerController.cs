using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;
using BioFarme.Audio;
using BioFarme.Farming;

namespace BioFarme.Player
{
    /// <summary>
    /// Hazmat Bio-Engineer player controller:
    /// - Smooth WASD/Arrow movement (5.4 base / 8.0 sprint with stamina) & grid collision sliding
    /// - Grid cursor highlight & valid/affordable ghost preview (works in Playing AND TacticalPause)
    /// - Active tools: CultivatorHoe (till/harvest), BioSprayer (LMB AoE acid cone + RMB 3x3 mist irrigation),
    ///   BoneHammer (LMB +60 HP repair for 5 Biomass, RMB demolish), Seed planting, and drag-paint Structure building
    /// - [E] proximity interaction prompt & handler for Mature Crops, MutagenIncubator, and LivingHeartCore
    /// </summary>
    [DefaultExecutionOrder(0)]
    public sealed class PlayerController : MonoBehaviour
    {
        public static readonly Vector3 DefaultSpawnPosition = new Vector3(18.5f, 8.5f, 0f);

        private const float BaseMoveSpeed = 5.4f;
        private const float SprintMoveSpeed = 8.0f;
        private const float StaminaDrainPerSec = 0.28f;
        private const float StaminaRegenPerSec = 0.24f;
        private const float CollisionHalfWidth = 0.25f;
        private const float CollisionHalfHeight = 0.21f;

        private const float HoeUseRange = 4.5f;
        private const float SprayerCombatRange = 4.2f;
        private const float SprayerConeAngleDeg = 55f;
        private const float SprayerNormalDps = 28f;
        private const float SprayerBuffedDps = 45f;
        private const float SprayerDamageTickInterval = 0.15f;
        private const float SprayerMistRange = 5.8f;

        private const float HammerUseRange = 4.6f;
        private const float HammerRepairHealAmount = 60f;
        private const float HammerRepairBiomassCost = 5f;

        private const float InteractionScanRadius = 2.8f;

        private static PlayerController _instance;
        private static bool _applicationQuitting;
        private static Sprite _cachedCursorFrameSprite;
        private static Sprite _cachedRangeRingSprite;

        public static PlayerController InstanceIfExists => _instance;

        public static PlayerController Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PlayerController>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[Player_HazmatEngineer]");
                        go.transform.position = DefaultSpawnPosition;
                        _instance = go.AddComponent<PlayerController>();
                    }
                }
                return _instance;
            }
        }

        private static bool _sceneHookRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrapPlayerAndFarmingSystems()
        {
            if (_applicationQuitting) return;

            if (!_sceneHookRegistered)
            {
                _sceneHookRegistered = true;
                SceneManager.sceneLoaded += OnSceneLoadedBootstrap;
            }

            EnsureCompanionSingletonsPresent();
        }

        private static void OnSceneLoadedBootstrap(Scene scene, LoadSceneMode mode)
        {
            if (_applicationQuitting) return;
            EnsureCompanionSingletonsPresent();
        }

        private static void EnsureCompanionSingletonsPresent()
        {
            if (_applicationQuitting) return;
            if (string.Equals(SceneManager.GetActiveScene().name, "MainMenu", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Only spawn missing systems if a scene/UI bootstrapper hasn't already instantiated them
            if (ToolHotbarSystem.InstanceIfExists == null) _ = ToolHotbarSystem.Instance;
            if (BioWeatherSystem.InstanceIfExists == null) _ = BioWeatherSystem.Instance;
            if (CropSystem.InstanceIfExists == null) _ = CropSystem.Instance;
            if (PlayerController.InstanceIfExists == null) _ = PlayerController.Instance;
            if (CameraFollowController.InstanceIfExists == null) _ = CameraFollowController.Instance;
        }

        // Player body & visuals
        private Transform _visualRoot;
        private SpriteRenderer _bodyRenderer;
        private SpriteRenderer _shadowRenderer;

        // BioSprayer combat cone visuals
        private Transform _muzzlePivot;
        private SpriteRenderer _flameConeOuterRenderer;
        private SpriteRenderer _acidSprayInnerRenderer;

        // Detached world-space grid cursor & ghost preview
        private GameObject _cursorRootGo;
        private Transform _cursorBoxTransform;
        private SpriteRenderer _cursorBoxRenderer;
        private SpriteRenderer _ghostPreviewRenderer;
        private SpriteRenderer _rangePreviewRenderer;

        // Detached world-space [E] floating prompt UI
        private GameObject _promptCanvasGo;
        private Text _promptText;
        private Image _promptBgImage;

        // Movement & Stamina state
        private float _staminaNormalized = 1f;
        private bool _sprintExhaustedLock;
        private float _staminaRegenDelayTimer;
        private Vector2 _aimDir = Vector2.down;
        private float _walkAnimTimer;
        private int _walkAnimFrame;
        private float _walkBobPhase;
        private float _swingRecoilTimer;
        private Vector2 _swingRecoilDir;

        // Action cooldowns & drag-paint state
        private float _toolCooldownTimer;
        private float _sprayDamageTickTimer;
        private float _sprayDropletTimer;
        private float _sprayVfxAnimTimer;
        private bool _isSprayingCombat;

        private Vector2Int _hoveredCell = new Vector2Int(18, 8);
        private Vector2Int _lastDragSeedCell = new Vector2Int(-999, -999);
        private Vector2Int _lastDragBuildCell = new Vector2Int(-999, -999);
        private Vector2Int _lastDragDemolishCell = new Vector2Int(-999, -999);

        // Interaction target state
        private IInteractable _nearestInteractable;
        private ICropEntity _nearestFallbackMatureCrop;
        private Vector3 _nearestInteractableWorldPos;
        private string _nearestPromptLabel = string.Empty;

        private readonly List<IDamageable> _enemyQueryBuffer = new List<IDamageable>(64);

        public Vector2Int HoveredCell => _hoveredCell;
        public float StaminaNormalized => _staminaNormalized;
        public bool IsSpraying => _isSprayingCombat;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            transform.position = DefaultSpawnPosition;

            BuildPlayerVisuals();
            BuildSprayerConeVisuals();
            BuildGridCursorVisuals();
            BuildInteractionPromptUi();

            WorldEntityRegistry.RegisterPlayer(transform);
            WorldEntityRegistry.PlayerStaminaNormalized = 1f;
            WorldEntityRegistry.PlayerHealthNormalized = 1f;
        }

        private void OnEnable()
        {
            WorldEntityRegistry.RegisterPlayer(transform);
            GameEvents.OnNewGameSessionInitialized += HandleNewGameSessionInitialized;
            GameEvents.OnGameStateChanged += HandleGameStateChanged;
        }

        private void Start()
        {
            // Ensure companion singletons are ready
            _ = ToolHotbarSystem.Instance;
            _ = CropSystem.Instance;
            _ = BioWeatherSystem.Instance;
            _ = CameraFollowController.Instance;
        }

        private void OnDisable()
        {
            StopSprayerEffects();
            GameEvents.OnNewGameSessionInitialized -= HandleNewGameSessionInitialized;
            GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        }

        private void OnDestroy()
        {
            StopSprayerEffects();

            if (_cursorRootGo != null)
            {
                Destroy(_cursorRootGo);
            }
            if (_promptCanvasGo != null)
            {
                Destroy(_promptCanvasGo);
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

        private void HandleNewGameSessionInitialized()
        {
            StopSprayerEffects();
            transform.position = DefaultSpawnPosition;
            _staminaNormalized = 1f;
            _sprintExhaustedLock = false;
            _staminaRegenDelayTimer = 0f;
            _toolCooldownTimer = 0f;
            _swingRecoilTimer = 0f;
            ResetDragMemory();

            WorldEntityRegistry.RegisterPlayer(transform);
            WorldEntityRegistry.PlayerStaminaNormalized = 1f;
            WorldEntityRegistry.PlayerHealthNormalized = 1f;
        }

        private void HandleGameStateChanged(GameState oldState, GameState newState)
        {
            if (newState != GameState.Playing)
            {
                StopSprayerEffects();
            }
            ResetDragMemory();
        }

        private void BuildPlayerVisuals()
        {
            // Ground contact shadow
            GameObject shadowGo = new GameObject("PlayerShadow");
            shadowGo.transform.SetParent(transform, false);
            shadowGo.transform.localPosition = new Vector3(0f, -0.34f, 0f);
            shadowGo.transform.localScale = new Vector3(0.75f, 0.34f, 1f);
            _shadowRenderer = shadowGo.AddComponent<SpriteRenderer>();
            _shadowRenderer.sprite = SpriteRegistry.GetSoftCircleSprite();
            _shadowRenderer.color = new Color(0.02f, 0.01f, 0.04f, 0.52f);
            _shadowRenderer.sortingOrder = 48;

            // Bobbing/recoil child transform for Hazmat sprite
            GameObject visGo = new GameObject("HazmatVisual");
            visGo.transform.SetParent(transform, false);
            _visualRoot = visGo.transform;

            _bodyRenderer = visGo.AddComponent<SpriteRenderer>();
            _bodyRenderer.sortingOrder = 50;
            _bodyRenderer.sprite = SpriteRegistry.GetPlayerDirectionalSprite(Vector2.down, 0, false);
        }

        private void BuildSprayerConeVisuals()
        {
            GameObject pivotGo = new GameObject("BioSprayerMuzzlePivot");
            pivotGo.transform.SetParent(transform, false);
            pivotGo.transform.localPosition = new Vector3(0.36f, -0.10f, 0f);
            _muzzlePivot = pivotGo.transform;

            // Outer wide bio-flame cone (VFX_BioFlame_Cone: 288x168 @ 128 PPU = 2.25u wide, narrow nozzle on left)
            GameObject coneGo = new GameObject("BioFlameConeOuter");
            coneGo.transform.SetParent(_muzzlePivot, false);
            coneGo.transform.localPosition = new Vector3(1.68f, 0f, 0f);
            coneGo.transform.localScale = new Vector3(1.48f, 1.38f, 1f);
            _flameConeOuterRenderer = coneGo.AddComponent<SpriteRenderer>();
            _flameConeOuterRenderer.sprite = SpriteRegistry.Get("VFX_BioFlame_Cone");
            _flameConeOuterRenderer.sortingOrder = 53;

            // Inner flickering acid jet (VFX_AcidSpray_01 / VFX_AcidSpray_02: 208x112 @ 128 PPU = 1.625u wide)
            GameObject jetGo = new GameObject("AcidSprayInner");
            jetGo.transform.SetParent(_muzzlePivot, false);
            jetGo.transform.localPosition = new Vector3(1.12f, 0f, 0f);
            jetGo.transform.localScale = new Vector3(1.35f, 1.15f, 1f);
            _acidSprayInnerRenderer = jetGo.AddComponent<SpriteRenderer>();
            _acidSprayInnerRenderer.sprite = SpriteRegistry.Get("VFX_AcidSpray_01");
            _acidSprayInnerRenderer.sortingOrder = 54;

            _muzzlePivot.gameObject.SetActive(false);
        }

        private void BuildGridCursorVisuals()
        {
            _cursorRootGo = new GameObject("[PlayerGridCursor]");

            // Range indicator ring (shown when previewing turrets, harvesters, mend tendrils, plasma veins)
            GameObject rangeGo = new GameObject("RangeRingPreview");
            rangeGo.transform.SetParent(_cursorRootGo.transform, false);
            _rangePreviewRenderer = rangeGo.AddComponent<SpriteRenderer>();
            _rangePreviewRenderer.sprite = GetOrCreateRangeRingSprite();
            _rangePreviewRenderer.sortingOrder = 43;
            _rangePreviewRenderer.enabled = false;

            // Semi-transparent ghost preview sprite
            GameObject ghostGo = new GameObject("PlacementGhost");
            ghostGo.transform.SetParent(_cursorRootGo.transform, false);
            _ghostPreviewRenderer = ghostGo.AddComponent<SpriteRenderer>();
            _ghostPreviewRenderer.sortingOrder = 45;
            _ghostPreviewRenderer.enabled = false;

            // Crisp corner-bracket highlight box
            GameObject boxGo = new GameObject("CursorHighlightBox");
            boxGo.transform.SetParent(_cursorRootGo.transform, false);
            _cursorBoxTransform = boxGo.transform;
            _cursorBoxRenderer = boxGo.AddComponent<SpriteRenderer>();
            _cursorBoxRenderer.sprite = GetOrCreateCursorFrameSprite();
            _cursorBoxRenderer.sortingOrder = 46;

            _cursorRootGo.SetActive(false);
        }

        private void BuildInteractionPromptUi()
        {
            _promptCanvasGo = new GameObject("[PlayerInteractionPromptCanvas]");
            Canvas canvas = _promptCanvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 68;

            RectTransform rootRect = _promptCanvasGo.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(560f, 50f);
            rootRect.localScale = Vector3.one * 0.0062f;

            GameObject bgGo = new GameObject("PromptBadgeBg");
            bgGo.transform.SetParent(_promptCanvasGo.transform, false);
            RectTransform bgRect = bgGo.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            _promptBgImage = bgGo.AddComponent<Image>();
            _promptBgImage.sprite = SpriteRegistry.GetWhiteSquareSprite();
            _promptBgImage.color = new Color(0.06f, 0.09f, 0.07f, 0.90f);
            _promptBgImage.raycastTarget = false;

            Outline bgOutline = bgGo.AddComponent<Outline>();
            bgOutline.effectColor = new Color(0.32f, 0.96f, 0.68f, 0.85f);
            bgOutline.effectDistance = new Vector2(2f, -2f);

            GameObject txtGo = new GameObject("PromptLabel");
            txtGo.transform.SetParent(bgGo.transform, false);
            RectTransform txtRect = txtGo.AddComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector2(12f, 4f);
            txtRect.offsetMax = new Vector2(-12f, -4f);

            _promptText = txtGo.AddComponent<Text>();
            _promptText.font = WorldPopupManager.GetBuiltinFont();
            _promptText.fontSize = 22;
            _promptText.fontStyle = FontStyle.Bold;
            _promptText.alignment = TextAnchor.MiddleCenter;
            _promptText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _promptText.verticalOverflow = VerticalWrapMode.Truncate;
            _promptText.resizeTextForBestFit = true;
            _promptText.resizeTextMinSize = 15;
            _promptText.resizeTextMaxSize = 22;
            _promptText.color = new Color(0.86f, 1.0f, 0.92f, 1f);
            _promptText.raycastTarget = false;

            Outline txtOutline = txtGo.AddComponent<Outline>();
            txtOutline.effectColor = new Color(0.03f, 0.01f, 0.05f, 0.95f);
            txtOutline.effectDistance = new Vector2(1.5f, -1.5f);

            _promptCanvasGo.SetActive(false);
        }

        private void Update()
        {
            WorldEntityRegistry.RegisterPlayer(transform);

            GameState state = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.State
                : GameState.Playing;

            bool isInteractive = state == GameState.Playing || state == GameState.TacticalPause;
            if (!isInteractive)
            {
                StopSprayerEffects();
                if (_cursorRootGo != null && _cursorRootGo.activeSelf) _cursorRootGo.SetActive(false);
                if (_promptCanvasGo != null && _promptCanvasGo.activeSelf) _promptCanvasGo.SetActive(false);
                return;
            }

            float dt = Time.deltaTime;
            if (_toolCooldownTimer > 0f)
            {
                _toolCooldownTimer = Mathf.Max(0f, _toolCooldownTimer - Time.unscaledDeltaTime);
            }

            // Reset drag-paint tracking when mouse buttons are released
            if (!Input.GetMouseButton(0))
            {
                _lastDragSeedCell = new Vector2Int(-999, -999);
                _lastDragBuildCell = new Vector2Int(-999, -999);
            }
            if (!Input.GetMouseButton(1))
            {
                _lastDragDemolishCell = new Vector2Int(-999, -999);
            }

            // 1. Update mouse world position, aim vector, and hovered grid cell
            Vector2 mouseWorld = ComputeMouseWorldPosition();
            Vector2 toMouse = mouseWorld - (Vector2)transform.position;
            if (toMouse.sqrMagnitude > 0.002f)
            {
                _aimDir = toMouse.normalized;
            }

            if (BioGridManager.InstanceIfExists != null)
            {
                _hoveredCell = BioGridManager.InstanceIfExists.WorldToGrid(mouseWorld);
            }

            // 2. Movement, Sprint, Stamina & Hazmat Sprite Animation (Active in Playing; paused in TacticalPause)
            if (state == GameState.Playing)
            {
                EnsureNotTrappedInStructure();
                HandleMovementAndStamina(dt);
            }
            else
            {
                StopSprayerEffects();
            }

            // 3. Update Grid Cursor & Ghost Preview (Active in BOTH Playing and TacticalPause)
            HotbarSlotData activeSlot = ToolHotbarSystem.Instance != null
                ? ToolHotbarSystem.Instance.SelectedSlot
                : null;

            UpdateCursorAndGhostPreview(activeSlot, state);

            // 4. Process Active Tool / Seed / Build Mouse Actions (guarded against UI clicks)
            bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!pointerOverUi && activeSlot != null)
            {
                HandleActiveSlotActions(activeSlot, state, mouseWorld, dt);
            }
            else
            {
                StopSprayerEffects();
            }

            // 5. Update Hazmat Directional Sprite, Flip, Walk Bobbing & Tool Recoil
            UpdatePlayerVisualAnimation(state, dt);

            // 6. Proximity [E] Interaction Scan & Floating World Prompt
            UpdateProximityInteraction(state);
        }

        // --- Movement, Sprint, Stamina & Grid Collision ---

        private void HandleMovementAndStamina(float dt)
        {
            if (dt <= 0f) return;

            float hx = 0f;
            float vy = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) hx -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) hx += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) vy -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) vy += 1f;

            Vector2 moveInput = new Vector2(hx, vy);
            bool hasMoveInput = moveInput.sqrMagnitude > 0.001f;
            if (moveInput.sqrMagnitude > 1f)
            {
                moveInput.Normalize();
            }

            bool wantsSprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (_sprintExhaustedLock && _staminaNormalized >= 0.18f)
            {
                _sprintExhaustedLock = false;
            }

            bool isSprinting = hasMoveInput && wantsSprint && !_sprintExhaustedLock && _staminaNormalized > 0.01f;

            if (isSprinting)
            {
                _staminaNormalized = Mathf.Max(0f, _staminaNormalized - StaminaDrainPerSec * dt);
                _staminaRegenDelayTimer = 0.35f;
                if (_staminaNormalized <= 0.005f)
                {
                    _sprintExhaustedLock = true;
                }
            }
            else
            {
                if (_staminaRegenDelayTimer > 0f)
                {
                    _staminaRegenDelayTimer = Mathf.Max(0f, _staminaRegenDelayTimer - dt);
                }
                else if (_staminaNormalized < 1f)
                {
                    _staminaNormalized = Mathf.Min(1f, _staminaNormalized + StaminaRegenPerSec * dt);
                }
            }

            WorldEntityRegistry.PlayerStaminaNormalized = _staminaNormalized;

            if (!hasMoveInput)
            {
                _walkAnimFrame = 0;
                return;
            }

            float speed = isSprinting ? SprintMoveSpeed : BaseMoveSpeed;
            // Slight mobility brace while actively firing heavy combat sprayer
            if (_isSprayingCombat)
            {
                speed *= 0.88f;
            }

            Vector2 currentPos = transform.position;
            Vector2 delta = moveInput * (speed * dt);
            Vector2 resolvedPos = MoveWithGridSliding(currentPos, delta);
            transform.position = new Vector3(resolvedPos.x, resolvedPos.y, 0f);

            // Walk frame & bobbing timers
            float animRate = isSprinting ? 11.5f : 7.8f;
            _walkAnimTimer += dt * animRate;
            _walkAnimFrame = Mathf.FloorToInt(_walkAnimTimer) % 2;
            _walkBobPhase += dt * (isSprinting ? 17f : 12.5f);
        }

        private Vector2 MoveWithGridSliding(Vector2 startPos, Vector2 delta)
        {
            Vector2 pos = startPos;

            // Sweep X
            if (Mathf.Abs(delta.x) > 0.00001f)
            {
                Vector2 candidateX = new Vector2(pos.x + delta.x, pos.y);
                if (IsFootprintWalkable(candidateX))
                {
                    pos.x = candidateX.x;
                }
                else if (Mathf.Abs(delta.y) < 0.001f)
                {
                    // Gentle corner assist when brushing past a wall edge horizontally
                    const float nudge = 0.06f;
                    if (IsFootprintWalkable(new Vector2(pos.x + delta.x * 0.6f, pos.y + nudge)))
                    {
                        pos.y += nudge;
                    }
                    else if (IsFootprintWalkable(new Vector2(pos.x + delta.x * 0.6f, pos.y - nudge)))
                    {
                        pos.y -= nudge;
                    }
                }
            }

            // Sweep Y
            if (Mathf.Abs(delta.y) > 0.00001f)
            {
                Vector2 candidateY = new Vector2(pos.x, pos.y + delta.y);
                if (IsFootprintWalkable(candidateY))
                {
                    pos.y = candidateY.y;
                }
                else if (Mathf.Abs(delta.x) < 0.001f)
                {
                    // Gentle corner assist when brushing past a wall edge vertically
                    const float nudge = 0.06f;
                    if (IsFootprintWalkable(new Vector2(pos.x + nudge, pos.y + delta.y * 0.6f)))
                    {
                        pos.x += nudge;
                    }
                    else if (IsFootprintWalkable(new Vector2(pos.x - nudge, pos.y + delta.y * 0.6f)))
                    {
                        pos.x -= nudge;
                    }
                }
            }

            // Final cavern map bounds safety clamp
            pos.x = Mathf.Clamp(pos.x, 1.25f, BioGridManager.Width - 1.25f);
            pos.y = Mathf.Clamp(pos.y, 1.25f, BioGridManager.Height - 1.25f);
            return pos;
        }

        private static bool IsFootprintWalkable(Vector2 worldPos)
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return true;

            if (worldPos.x < 1.15f || worldPos.x > BioGridManager.Width - 1.15f ||
                worldPos.y < 1.15f || worldPos.y > BioGridManager.Height - 1.15f)
            {
                return false;
            }

            // Sample 4 bounding corners around the player's boots/torso
            Vector2 c0 = new Vector2(worldPos.x - CollisionHalfWidth, worldPos.y - CollisionHalfHeight);
            Vector2 c1 = new Vector2(worldPos.x + CollisionHalfWidth, worldPos.y - CollisionHalfHeight);
            Vector2 c2 = new Vector2(worldPos.x - CollisionHalfWidth, worldPos.y + CollisionHalfHeight);
            Vector2 c3 = new Vector2(worldPos.x + CollisionHalfWidth, worldPos.y + CollisionHalfHeight);

            return grid.IsWalkable(grid.WorldToGrid(c0))
                && grid.IsWalkable(grid.WorldToGrid(c1))
                && grid.IsWalkable(grid.WorldToGrid(c2))
                && grid.IsWalkable(grid.WorldToGrid(c3));
        }

        /// <summary>
        /// If the player built an impassable wall/turret directly on their own tile, smoothly eject them
        /// to the closest adjacent walkable tile so they can never trap themselves.
        /// </summary>
        private void EnsureNotTrappedInStructure()
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return;

            Vector2 pos = transform.position;
            if (IsFootprintWalkable(pos)) return;

            Vector2Int curCell = grid.WorldToGrid(pos);
            float bestDistSq = float.MaxValue;
            Vector2 bestTarget = pos;
            bool found = false;

            for (int radius = 1; radius <= 3 && !found; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        Vector2Int candidateCell = new Vector2Int(curCell.x + dx, curCell.y + dy);
                        if (!grid.IsWalkable(candidateCell)) continue;
                        Vector2 center = grid.GridToWorldCenter(candidateCell);
                        if (!IsFootprintWalkable(center)) continue;

                        float dSq = (center - pos).sqrMagnitude;
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            bestTarget = center;
                            found = true;
                        }
                    }
                }
            }

            if (found)
            {
                transform.position = new Vector3(bestTarget.x, bestTarget.y, 0f);
            }
        }

        // --- Active Tool, Seed & Structure Actions ---

        private void HandleActiveSlotActions(HotbarSlotData slot, GameState state, Vector2 mouseWorld, float dt)
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return;

            switch (slot.category)
            {
                case HotbarItemCategory.Tool:
                    HandleToolSlotAction(slot.toolType, state, grid, mouseWorld, dt);
                    break;

                case HotbarItemCategory.Seed:
                    StopSprayerEffects();
                    HandleSeedSlotAction(slot.cropType, grid);
                    break;

                case HotbarItemCategory.Structure:
                    StopSprayerEffects();
                    HandleStructureSlotAction(slot.structureType, grid);
                    break;
            }
        }

        private void HandleToolSlotAction(ToolType toolType, GameState state, BioGridManager grid, Vector2 mouseWorld, float dt)
        {
            if (toolType != ToolType.BioSprayer)
            {
                StopSprayerEffects();
            }

            switch (toolType)
            {
                case ToolType.CultivatorHoe:
                    if (state == GameState.Playing && Input.GetMouseButton(0) && _toolCooldownTimer <= 0f)
                    {
                        UseCultivatorHoe(grid);
                    }
                    break;

                case ToolType.BioSprayer:
                    HandleBioSprayer(state, grid, mouseWorld, dt);
                    break;

                case ToolType.BoneHammer:
                    HandleBoneHammer(state, grid);
                    break;
            }
        }

        private void UseCultivatorHoe(BioGridManager grid)
        {
            if (!grid.IsInBounds(_hoveredCell)) return;

            Vector2 cellCenter = grid.GridToWorldCenter(_hoveredCell);
            float dist = Vector2.Distance(transform.position, cellCenter);
            if (dist > HoeUseRange) return;

            // Priority 1: Harvest mature crop at cell
            ICropEntity crop = WorldEntityRegistry.GetCropAt(_hoveredCell) ?? grid.GetCropAt(_hoveredCell);
            if (crop != null && crop.IsMature)
            {
                crop.Harvest();
                TriggerSwingRecoil(cellCenter - (Vector2)transform.position);
                _toolCooldownTimer = 0.16f;
                return;
            }

            // Priority 2: Till FleshSoil / MossFertile into TilledBed
            if (grid.CanTill(_hoveredCell) && grid.TillTile(_hoveredCell))
            {
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxTill, cellCenter, 0.78f, 0.08f);
                }

                if (BioWeatherSystem.InstanceIfExists != null)
                {
                    Color soilColor = grid.IsTileFertile(_hoveredCell)
                        ? new Color(0.32f, 0.72f, 0.42f, 0.85f)
                        : new Color(0.55f, 0.30f, 0.34f, 0.85f);
                    BioWeatherSystem.InstanceIfExists.SpawnPuffBurst(cellCenter, soilColor, 7, 1.0f);
                }

                TriggerSwingRecoil(cellCenter - (Vector2)transform.position);
                _toolCooldownTimer = 0.18f;
            }
        }

        private void HandleBioSprayer(GameState state, BioGridManager grid, Vector2 mouseWorld, float dt)
        {
            if (state != GameState.Playing)
            {
                StopSprayerEffects();
                return;
            }

            bool lmbHeld = Input.GetMouseButton(0);
            bool rmbHeld = Input.GetMouseButton(1);

            // LMB Hold: Combat Bio-Flamethrower / Acid Sprayer + Cone Irrigation
            if (lmbHeld)
            {
                _isSprayingCombat = true;
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.SetSprayLoopActive(true, AudioKeys.SfxSprayLoop, 0.65f);
                }

                UpdateSprayerConeVisuals(dt);

                // Emit pressurized acid droplets along muzzle
                _sprayDropletTimer -= dt;
                if (_sprayDropletTimer <= 0f)
                {
                    _sprayDropletTimer = 0.045f;
                    if (BioWeatherSystem.InstanceIfExists != null)
                    {
                        bool buffed = ResourceManager.InstanceIfExists != null && ResourceManager.InstanceIfExists.HasActiveBuff;
                        Color dropCol = buffed
                            ? new Color(0.95f, 0.38f, 0.98f, 0.65f)
                            : new Color(0.38f, 1.0f, 0.56f, 0.65f);
                        Vector2 muzzlePos = (Vector2)transform.position + _aimDir * 0.48f;
                        BioWeatherSystem.InstanceIfExists.SpawnDirectionalSprayDroplets(muzzlePos, _aimDir, dropCol, 2, SprayerCombatRange);
                    }
                }

                // Apply continuous AoE cone damage & cone irrigation at crisp 0.15s intervals
                _sprayDamageTickTimer -= dt;
                if (_sprayDamageTickTimer <= 0f)
                {
                    _sprayDamageTickTimer = SprayerDamageTickInterval;
                    PerformSprayerConeDamageAndIrrigation(grid, SprayerDamageTickInterval);
                }
            }
            else
            {
                StopSprayerEffects();
            }

            // RMB Hold/Click: 3x3 Nutrient Mist Irrigation
            if (rmbHeld && !lmbHeld && _toolCooldownTimer <= 0f && grid.IsInBounds(_hoveredCell))
            {
                Vector2 targetCenter = grid.GridToWorldCenter(_hoveredCell);
                if (Vector2.Distance(transform.position, targetCenter) <= SprayerMistRange)
                {
                    bool anyWatered = false;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            Vector2Int c = new Vector2Int(_hoveredCell.x + dx, _hoveredCell.y + dy);
                            if (grid.WaterTile(c, 1.0f))
                            {
                                anyWatered = true;
                            }
                        }
                    }

                    if (AudioManager.Instance != null)
                    {
                        AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxWater, targetCenter, 0.80f, 0.08f);
                    }

                    if (BioWeatherSystem.InstanceIfExists != null)
                    {
                        BioWeatherSystem.InstanceIfExists.SpawnPuffBurst(
                            targetCenter,
                            new Color(0.24f, 0.95f, 0.92f, 0.80f),
                            anyWatered ? 12 : 7,
                            1.35f);
                    }

                    TriggerSwingRecoil(targetCenter - (Vector2)transform.position, 0.06f);
                    _toolCooldownTimer = 0.24f;
                }
            }
        }

        private void UpdateSprayerConeVisuals(float dt)
        {
            if (_muzzlePivot == null) return;

            if (!_muzzlePivot.gameObject.activeSelf)
            {
                _muzzlePivot.gameObject.SetActive(true);
            }

            float facingSign = _aimDir.x < -0.05f ? -1f : 1f;
            Vector2 nozzleLocal = new Vector2(facingSign * 0.34f, -0.10f) + _aimDir * 0.14f;
            _muzzlePivot.localPosition = new Vector3(nozzleLocal.x, nozzleLocal.y, 0f);

            float angleDeg = Mathf.Atan2(_aimDir.y, _aimDir.x) * Mathf.Rad2Deg;
            _muzzlePivot.localRotation = Quaternion.Euler(0f, 0f, angleDeg);

            _sprayVfxAnimTimer += dt * 16f;
            int frame = Mathf.FloorToInt(_sprayVfxAnimTimer) % 2;
            float pulse = 1f + Mathf.Sin(_sprayVfxAnimTimer * 1.7f) * 0.09f;

            bool hasBuff = ResourceManager.InstanceIfExists != null && ResourceManager.InstanceIfExists.HasActiveBuff;

            if (_flameConeOuterRenderer != null)
            {
                _flameConeOuterRenderer.sprite = SpriteRegistry.Get("VFX_BioFlame_Cone");
                _flameConeOuterRenderer.color = hasBuff
                    ? new Color(0.95f, 0.48f, 1.0f, 0.82f)
                    : new Color(0.55f, 1.0f, 0.58f, 0.80f);
                _flameConeOuterRenderer.transform.localScale = new Vector3(1.52f * pulse, 1.42f * (2f - pulse), 1f);
            }

            if (_acidSprayInnerRenderer != null)
            {
                _acidSprayInnerRenderer.sprite = SpriteRegistry.Get(frame == 0 ? "VFX_AcidSpray_01" : "VFX_AcidSpray_02");
                _acidSprayInnerRenderer.color = hasBuff
                    ? new Color(1.0f, 0.80f, 1.0f, 0.92f)
                    : new Color(0.75f, 1.0f, 0.82f, 0.90f);
                _acidSprayInnerRenderer.transform.localScale = new Vector3(1.36f * (2f - pulse), 1.18f * pulse, 1f);
            }
        }

        private void PerformSprayerConeDamageAndIrrigation(BioGridManager grid, float tickDuration)
        {
            Vector2 origin = transform.position;
            float cosHalfCone = Mathf.Cos((SprayerConeAngleDeg * 0.5f) * Mathf.Deg2Rad);

            bool hasBuff = ResourceManager.InstanceIfExists != null && ResourceManager.InstanceIfExists.HasActiveBuff;
            float dps = hasBuff ? SprayerBuffedDps : SprayerNormalDps;
            float damagePerTick = dps * tickDuration;

            // 1. Damage all enemies inside 4.2f radius & 55° cone
            int count = WorldEntityRegistry.GetEnemiesInRadius(origin, SprayerCombatRange, _enemyQueryBuffer);
            for (int i = 0; i < count; i++)
            {
                IDamageable enemy = _enemyQueryBuffer[i];
                if (enemy == null || !enemy.IsAlive) continue;

                Vector2 toEnemy = enemy.WorldPosition - origin;
                float dist = toEnemy.magnitude;
                if (dist > SprayerCombatRange) continue;

                bool inCone = dist < 0.65f
                    ? Vector2.Dot(toEnemy.normalized, _aimDir) > 0.15f
                    : Vector2.Dot(toEnemy / dist, _aimDir) >= cosHalfCone;

                if (inCone)
                {
                    enemy.TakeDamage(damagePerTick, origin);
                }
            }

            // 2. Auto-irrigate any TilledBed tiles inside the spray cone
            Vector2Int playerCell = grid.WorldToGrid(origin);
            int cellRad = Mathf.CeilToInt(SprayerCombatRange);
            for (int dx = -cellRad; dx <= cellRad; dx++)
            {
                for (int dy = -cellRad; dy <= cellRad; dy++)
                {
                    Vector2Int cell = new Vector2Int(playerCell.x + dx, playerCell.y + dy);
                    if (!grid.CanWater(cell)) continue;

                    Vector2 toTile = (Vector2)grid.GridToWorldCenter(cell) - origin;
                    float dist = toTile.magnitude;
                    if (dist > SprayerCombatRange || dist < 0.05f) continue;

                    if (dist < 0.85f || Vector2.Dot(toTile / dist, _aimDir) >= cosHalfCone)
                    {
                        grid.WaterTile(cell, 1.0f);
                    }
                }
            }
        }

        private void StopSprayerEffects()
        {
            if (_isSprayingCombat)
            {
                _isSprayingCombat = false;
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.SetSprayLoopActive(false);
                }
            }

            if (_muzzlePivot != null && _muzzlePivot.gameObject.activeSelf)
            {
                _muzzlePivot.gameObject.SetActive(false);
            }
        }

        private void HandleBoneHammer(GameState state, BioGridManager grid)
        {
            if (!grid.IsInBounds(_hoveredCell)) return;
            Vector2 cellCenter = grid.GridToWorldCenter(_hoveredCell);

            // LMB: Repair damaged friendly structure (+60 HP for 5 Biomass)
            if (state == GameState.Playing && Input.GetMouseButton(0) && _toolCooldownTimer <= 0f)
            {
                if (Vector2.Distance(transform.position, cellCenter) <= HammerUseRange)
                {
                    IDamageable dmg = grid.GetDamageableAt(_hoveredCell);
                    if (dmg != null && dmg.IsAlive && dmg.CurrentHp < dmg.MaxHp - 0.05f)
                    {
                        if (ResourceManager.Instance != null &&
                            ResourceManager.Instance.TrySpend(ResourceType.Biomass, HammerRepairBiomassCost))
                        {
                            float hpBefore = dmg.CurrentHp;
                            dmg.Heal(HammerRepairHealAmount);
                            float actualHealed = Mathf.Max(1f, dmg.CurrentHp - hpBefore);

                            // BuiltInStructureNode spawns its own heal popup; ensure non-BuiltIn structures also show a green popup
                            if (!(dmg is BuiltInStructureNode))
                            {
                                WorldPopupManager.SpawnHeal(dmg.WorldPosition, actualHealed);
                            }

                            if (AudioManager.Instance != null)
                            {
                                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxRepair, dmg.WorldPosition, 0.85f, 0.06f);
                            }

                            if (BioWeatherSystem.InstanceIfExists != null)
                            {
                                BioWeatherSystem.InstanceIfExists.SpawnPuffBurst(
                                    dmg.WorldPosition,
                                    new Color(0.38f, 1.0f, 0.58f, 0.90f),
                                    7,
                                    1.1f);
                            }

                            TriggerSwingRecoil(cellCenter - (Vector2)transform.position);
                            _toolCooldownTimer = 0.28f;
                        }
                        else
                        {
                            _toolCooldownTimer = 0.25f;
                        }
                    }
                }
            }

            // RMB: Demolish structure at hovered cell (works in Playing & TacticalPause)
            if (Input.GetMouseButton(1) && _hoveredCell != _lastDragDemolishCell && _toolCooldownTimer <= 0f)
            {
                if (TryDemolishAtHoveredCell(grid))
                {
                    _lastDragDemolishCell = _hoveredCell;
                    TriggerSwingRecoil(cellCenter - (Vector2)transform.position);
                    _toolCooldownTimer = 0.14f;
                }
            }
        }

        private void HandleSeedSlotAction(CropType cropType, BioGridManager grid)
        {
            if (cropType == CropType.None || !grid.IsInBounds(_hoveredCell)) return;

            if (Input.GetMouseButton(0) && _hoveredCell != _lastDragSeedCell && _toolCooldownTimer <= 0f)
            {
                // Cozy Stardew UX: if a mature crop sits on the hovered bed, harvest it before planting!
                ICropEntity existingCrop = WorldEntityRegistry.GetCropAt(_hoveredCell) ?? grid.GetCropAt(_hoveredCell);
                if (existingCrop != null && existingCrop.IsMature)
                {
                    existingCrop.Harvest();
                }

                if (GameEvents.TryRequestPlantCrop(_hoveredCell, cropType))
                {
                    _lastDragSeedCell = _hoveredCell;
                    Vector2 cellCenter = grid.GridToWorldCenter(_hoveredCell);
                    TriggerSwingRecoil(cellCenter - (Vector2)transform.position, 0.05f);
                    _toolCooldownTimer = 0.08f;
                }
                else if (Input.GetMouseButtonDown(0))
                {
                    _lastDragSeedCell = _hoveredCell;
                    _toolCooldownTimer = 0.12f;
                }
            }
        }

        private void HandleStructureSlotAction(StructureType structureType, BioGridManager grid)
        {
            if (structureType == StructureType.None || !grid.IsInBounds(_hoveredCell)) return;

            // LMB Hold/Drag: Paint walls, veins, turrets, and bio-reactors seamlessly across cells (Playing & TacticalPause!)
            if (Input.GetMouseButton(0) && _hoveredCell != _lastDragBuildCell && _toolCooldownTimer <= 0f)
            {
                bool built = false;
                if (GameEvents.BuildStructureHandler != null)
                {
                    built = GameEvents.TryRequestBuildStructure(_hoveredCell, structureType);
                }
                else
                {
                    // Standalone fallback if Factory module is absent during isolated tests
                    built = TryFallbackBuildStructure(grid, _hoveredCell, structureType);
                }

                if (built)
                {
                    _lastDragBuildCell = _hoveredCell;
                    Vector2 cellCenter = grid.GridToWorldCenter(_hoveredCell);
                    if (BioWeatherSystem.InstanceIfExists != null)
                    {
                        BioWeatherSystem.InstanceIfExists.SpawnPuffBurst(
                            cellCenter,
                            new Color(0.42f, 0.95f, 0.62f, 0.80f),
                            6,
                            1.0f);
                    }

                    EnsureNotTrappedInStructure();
                    TriggerSwingRecoil(cellCenter - (Vector2)transform.position, 0.05f);
                    _toolCooldownTimer = 0.06f;
                }
                else if (Input.GetMouseButtonDown(0))
                {
                    _lastDragBuildCell = _hoveredCell;
                    _toolCooldownTimer = 0.12f;
                }
            }

            // RMB Hold/Click while holding Structure: Mindustry-style quick deconstruct!
            if (Input.GetMouseButton(1) && _hoveredCell != _lastDragDemolishCell && _toolCooldownTimer <= 0f)
            {
                if (TryDemolishAtHoveredCell(grid))
                {
                    _lastDragDemolishCell = _hoveredCell;
                    _toolCooldownTimer = 0.12f;
                }
            }
        }

        private static bool TryDemolishAtHoveredCell(BioGridManager grid)
        {
            Vector2Int cell = Instance != null ? Instance._hoveredCell : Vector2Int.zero;
            if (!grid.IsInBounds(cell)) return false;

            if (GameEvents.DemolishStructureHandler != null)
            {
                bool ok = GameEvents.TryRequestDemolishStructure(cell);
                if (ok && BioWeatherSystem.InstanceIfExists != null)
                {
                    BioWeatherSystem.InstanceIfExists.SpawnPuffBurst(
                        grid.GridToWorldCenter(cell),
                        new Color(0.95f, 0.62f, 0.42f, 0.85f),
                        6,
                        1.0f);
                }
                return ok;
            }

            // Safe fallback for starter BuiltInStructureNode if Factory module isn't registered yet
            IGridEntity existing = grid.GetStructureAt(cell);
            if (existing == null) return false;
            StructureDefinition def = BalanceDatabase.GetStructure(existing.Type);
            if (def != null && !def.canDeconstruct) return false;

            Vector2 worldPos = grid.GridToWorldCenter(cell);
            if (def != null && ResourceManager.Instance != null)
            {
                ResourceManager.Instance.RefundPercentage(def.costs, BalanceDatabase.DeconstructRefundFraction);
            }

            grid.UnregisterStructure(cell);
            if (existing is MonoBehaviour mb && mb != null)
            {
                Destroy(mb.gameObject);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxDemolish, worldPos, 0.80f, 0.06f);
            }
            return true;
        }

        private static bool TryFallbackBuildStructure(BioGridManager grid, Vector2Int cell, StructureType type)
        {
            StructureDefinition def = BalanceDatabase.GetStructure(type);
            if (def == null || !grid.CanPlaceStructure(cell, type)) return false;
            if (ResourceManager.Instance != null && !ResourceManager.Instance.TrySpend(def.costs)) return false;

            BuiltInStructureNode node = grid.SpawnBuiltInStructure(cell, type, isStarter: false);
            if (node == null) return false;

            GameManager.InstanceIfExists?.RecordStructureBuilt();
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxAtPosition(AudioKeys.SfxBuild, grid.GridToWorldCenter(cell), 0.85f, 0.06f);
            }
            return true;
        }

        // --- Grid Cursor & Ghost Preview ---

        private void UpdateCursorAndGhostPreview(HotbarSlotData slot, GameState state)
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (_cursorRootGo == null || grid == null || slot == null || !grid.IsInBounds(_hoveredCell))
            {
                if (_cursorRootGo != null && _cursorRootGo.activeSelf) _cursorRootGo.SetActive(false);
                return;
            }

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (overUi)
            {
                if (_cursorRootGo.activeSelf) _cursorRootGo.SetActive(false);
                return;
            }

            if (!_cursorRootGo.activeSelf)
            {
                _cursorRootGo.SetActive(true);
            }

            Vector2Int footprint = Vector2Int.one;
            Sprite ghostSprite = null;
            bool showGhost = false;
            bool isValid = true;
            float rangeRadius = 0f;

            Color validFrameCol = new Color(0.35f, 1.0f, 0.58f, 0.88f);
            Color invalidFrameCol = new Color(1.0f, 0.30f, 0.34f, 0.88f);
            Color neutralFrameCol = new Color(0.55f, 0.92f, 1.0f, 0.58f);

            if (slot.category == HotbarItemCategory.Structure && slot.structureType != StructureType.None)
            {
                StructureDefinition def = BalanceDatabase.GetStructure(slot.structureType);
                footprint = def != null ? def.footprintSize : Vector2Int.one;
                showGhost = true;

                if (def != null && def.isConduit)
                {
                    ghostSprite = SpriteRegistry.GetVeinAutoTileSprite(slot.structureType, grid.GetVeinNeighborMask(_hoveredCell));
                }
                else if (slot.structureType == StructureType.BoneWall || slot.structureType == StructureType.OrganicFence)
                {
                    ghostSprite = SpriteRegistry.GetWallAutoTileSprite(slot.structureType, grid.GetWallNeighborMask(_hoveredCell));
                }
                else
                {
                    ghostSprite = SpriteRegistry.GetStructureSprite(slot.structureType);
                }

                bool canPlace = grid.CanPlaceStructure(_hoveredCell, slot.structureType);
                bool canAfford = def == null || ResourceManager.Instance == null || ResourceManager.Instance.CanAfford(def.costs);
                isValid = canPlace && canAfford;

                if (def != null && def.range >= 1.2f)
                {
                    rangeRadius = def.range;
                }

                _cursorBoxRenderer.color = isValid ? validFrameCol : invalidFrameCol;
            }
            else if (slot.category == HotbarItemCategory.Seed && slot.cropType != CropType.None)
            {
                CropDefinition cropDef = BalanceDatabase.GetCrop(slot.cropType);
                showGhost = true;
                ghostSprite = SpriteRegistry.GetCropStageSprite(slot.cropType, 3);

                ICropEntity matureCrop = WorldEntityRegistry.GetCropAt(_hoveredCell) ?? grid.GetCropAt(_hoveredCell);
                bool tileReadyOrTillable = grid.CanPlantCrop(_hoveredCell) || grid.CanTill(_hoveredCell) || (matureCrop != null && matureCrop.IsMature);
                bool canAfford = cropDef == null || ResourceManager.Instance == null || ResourceManager.Instance.CanAfford(cropDef.GetSeedCosts());
                isValid = tileReadyOrTillable && canAfford;

                _cursorBoxRenderer.color = isValid ? validFrameCol : invalidFrameCol;
            }
            else // Tool
            {
                showGhost = false;
                Vector2 cellCenter = grid.GridToWorldCenter(_hoveredCell);
                float dist = Vector2.Distance(transform.position, cellCenter);

                if (slot.toolType == ToolType.CultivatorHoe)
                {
                    ICropEntity crop = WorldEntityRegistry.GetCropAt(_hoveredCell) ?? grid.GetCropAt(_hoveredCell);
                    bool actionable = (crop != null && crop.IsMature) || grid.CanTill(_hoveredCell);
                    bool inRange = state == GameState.Playing && dist <= HoeUseRange;
                    _cursorBoxRenderer.color = (actionable && inRange)
                        ? validFrameCol
                        : (actionable ? new Color(1.0f, 0.78f, 0.28f, 0.62f) : new Color(0.7f, 0.85f, 0.78f, 0.32f));
                }
                else if (slot.toolType == ToolType.BioSprayer)
                {
                    // Expand highlight to 3x3 when holding RMB for nutrient mist irrigation
                    if (Input.GetMouseButton(1) && !Input.GetMouseButton(0))
                    {
                        footprint = new Vector2Int(3, 3);
                        _cursorRootGo.transform.position = grid.GridToWorldCenter(_hoveredCell);
                        _cursorBoxTransform.localScale = new Vector3(3f, 3f, 1f);
                        _cursorBoxRenderer.color = new Color(0.24f, 0.96f, 0.96f, 0.72f);
                        _ghostPreviewRenderer.enabled = false;
                        _rangePreviewRenderer.enabled = false;
                        return;
                    }
                    _cursorBoxRenderer.color = neutralFrameCol;
                }
                else if (slot.toolType == ToolType.BoneHammer)
                {
                    IDamageable dmg = grid.GetDamageableAt(_hoveredCell);
                    IGridEntity ent = grid.GetStructureAt(_hoveredCell);
                    if (dmg != null && dmg.IsAlive && dmg.CurrentHp < dmg.MaxHp - 0.05f && dist <= HammerUseRange)
                    {
                        bool affordRepair = ResourceManager.Instance == null || ResourceManager.Instance.CanAfford(ResourceType.Biomass, HammerRepairBiomassCost);
                        _cursorBoxRenderer.color = affordRepair ? validFrameCol : invalidFrameCol;
                    }
                    else if (ent != null)
                    {
                        _cursorBoxRenderer.color = new Color(1.0f, 0.62f, 0.25f, 0.75f);
                    }
                    else
                    {
                        _cursorBoxRenderer.color = new Color(0.75f, 0.75f, 0.82f, 0.30f);
                    }
                }
            }

            Vector3 centerWorld = grid.GetFootprintWorldCenter(_hoveredCell, footprint);
            _cursorRootGo.transform.position = centerWorld;
            _cursorBoxTransform.localScale = new Vector3(footprint.x, footprint.y, 1f);

            if (showGhost && ghostSprite != null)
            {
                _ghostPreviewRenderer.enabled = true;
                _ghostPreviewRenderer.sprite = ghostSprite;
                float ghostPulse = 0.54f + 0.10f * Mathf.Sin(Time.unscaledTime * 5.5f);
                _ghostPreviewRenderer.color = isValid
                    ? new Color(0.42f, 1.0f, 0.62f, ghostPulse)
                    : new Color(1.0f, 0.32f, 0.36f, ghostPulse);
            }
            else
            {
                _ghostPreviewRenderer.enabled = false;
            }

            if (showGhost && rangeRadius > 0.5f)
            {
                _rangePreviewRenderer.enabled = true;
                float diameter = rangeRadius * 2f;
                _rangePreviewRenderer.transform.localScale = new Vector3(diameter, diameter, 1f);
                _rangePreviewRenderer.color = isValid
                    ? new Color(0.32f, 0.96f, 0.82f, 0.26f)
                    : new Color(1.0f, 0.35f, 0.38f, 0.18f);
            }
            else
            {
                _rangePreviewRenderer.enabled = false;
            }
        }

        // --- Hazmat Sprite Direction, Walk Bobbing & Recoil ---

        private void UpdatePlayerVisualAnimation(GameState state, float dt)
        {
            if (_bodyRenderer == null || _visualRoot == null) return;

            bool moving = state == GameState.Playing &&
                          (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) ||
                           Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.RightArrow));

            Vector2 spriteDir = (moving || _isSprayingCombat) ? _aimDir : (Mathf.Abs(_aimDir.y) > 0.72f ? new Vector2(0f, _aimDir.y) : _aimDir);
            int frameToUse = moving ? _walkAnimFrame : 0;

            _bodyRenderer.sprite = SpriteRegistry.GetPlayerDirectionalSprite(spriteDir, frameToUse, _isSprayingCombat);
            _bodyRenderer.flipX = _aimDir.x < -0.05f;

            // Compute subtle walk bobbing + idle breathing + tool recoil
            float bobY = 0f;
            float tiltZ = 0f;
            float scaleX = 1f;
            float scaleY = 1f;

            if (moving)
            {
                float s = Mathf.Sin(_walkBobPhase);
                bobY = Mathf.Abs(s) * 0.065f;
                tiltZ = s * 3.2f;
                scaleX = 1f - Mathf.Abs(s) * 0.03f;
                scaleY = 1f + Mathf.Abs(s) * 0.04f;
            }
            else
            {
                float breath = Mathf.Sin(Time.unscaledTime * 2.7f) * 0.018f;
                scaleX = 1f - breath * 0.5f;
                scaleY = 1f + breath;
            }

            Vector2 recoilOffset = Vector2.zero;
            if (_swingRecoilTimer > 0f)
            {
                _swingRecoilTimer = Mathf.Max(0f, _swingRecoilTimer - Time.unscaledDeltaTime);
                float t = _swingRecoilTimer / 0.14f;
                float punch = Mathf.Sin(t * Mathf.PI);
                recoilOffset = _swingRecoilDir * (punch * 0.11f);
            }
            else if (_isSprayingCombat)
            {
                float jitter = Mathf.Sin(Time.time * 42f) * 0.022f;
                recoilOffset = -_aimDir * (0.04f + Mathf.Abs(jitter));
            }

            _visualRoot.localPosition = new Vector3(recoilOffset.x, bobY + recoilOffset.y, 0f);
            _visualRoot.localRotation = Quaternion.Euler(0f, 0f, tiltZ);
            _visualRoot.localScale = new Vector3(scaleX, scaleY, 1f);
        }

        private void TriggerSwingRecoil(Vector2 towardsTarget, float strengthScale = 1f)
        {
            _swingRecoilDir = towardsTarget.sqrMagnitude > 0.001f
                ? towardsTarget.normalized * strengthScale
                : _aimDir * strengthScale;
            _swingRecoilTimer = 0.14f;
        }

        // --- Proximity [E] Interaction & Floating World Prompt ---

        private void UpdateProximityInteraction(GameState state)
        {
            _nearestInteractable = null;
            _nearestFallbackMatureCrop = null;
            _nearestPromptLabel = string.Empty;

            if (state != GameState.Playing)
            {
                if (_promptCanvasGo != null && _promptCanvasGo.activeSelf) _promptCanvasGo.SetActive(false);
                return;
            }

            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null)
            {
                if (_promptCanvasGo != null && _promptCanvasGo.activeSelf) _promptCanvasGo.SetActive(false);
                return;
            }

            Vector2 playerPos = transform.position;
            Vector2Int playerCell = grid.WorldToGrid(playerPos);
            int cellScan = Mathf.CeilToInt(InteractionScanRadius) + 1;

            float bestScoreDist = InteractionScanRadius;

            for (int dx = -cellScan; dx <= cellScan; dx++)
            {
                for (int dy = -cellScan; dy <= cellScan; dy++)
                {
                    Vector2Int cell = new Vector2Int(playerCell.x + dx, playerCell.y + dy);
                    if (!grid.IsInBounds(cell)) continue;

                    Vector2 cellCenter = grid.GridToWorldCenter(cell);
                    float distToCell = Vector2.Distance(playerPos, cellCenter);
                    if (distToCell > InteractionScanRadius) continue;

                    // 1. Check structure IInteractable (MutagenIncubator, LivingHeartCore, etc.)
                    IGridEntity structure = grid.GetStructureAt(cell);
                    if (structure != null)
                    {
                        IInteractable structInteractable = structure as IInteractable;
                        if (structInteractable == null && structure is Component comp && comp != null)
                        {
                            structInteractable = comp.GetComponent<IInteractable>();
                        }

                        if (structInteractable != null && structInteractable.CanInteract())
                        {
                            // Slight priority bias (-0.25f) so Heart Core / Incubator prompts win ties over adjacent crops
                            float effectiveDist = distToCell - 0.25f;
                            if (effectiveDist < bestScoreDist)
                            {
                                string label = structInteractable.GetInteractionPrompt();
                                if (!string.IsNullOrEmpty(label))
                                {
                                    bestScoreDist = effectiveDist;
                                    _nearestInteractable = structInteractable;
                                    _nearestFallbackMatureCrop = null;
                                    StructureDefinition sDef = BalanceDatabase.GetStructure(structure.Type);
                                    Vector2Int fp = sDef != null ? sDef.footprintSize : Vector2Int.one;
                                    Vector3 centerWorld = grid.GetFootprintWorldCenter(structure.GridPos, fp);
                                    float yOffset = fp.y >= 3 ? (-fp.y * 0.50f - 0.16f) : (fp.y * 0.52f + 0.30f);
                                    _nearestInteractableWorldPos = centerWorld + new Vector3(0f, yOffset, 0f);
                                    _nearestPromptLabel = FormatPromptWithKeyPrefix(label);
                                }
                            }
                        }
                    }

                    // 2. Check mature ICropEntity at cell
                    ICropEntity crop = WorldEntityRegistry.GetCropAt(cell) ?? grid.GetCropAt(cell);
                    if (crop != null && crop.IsMature && distToCell < bestScoreDist)
                    {
                        bestScoreDist = distToCell;
                        if (crop is IInteractable cropInteract && cropInteract.CanInteract())
                        {
                            _nearestInteractable = cropInteract;
                            _nearestFallbackMatureCrop = null;
                            string label = cropInteract.GetInteractionPrompt();
                            _nearestPromptLabel = FormatPromptWithKeyPrefix(
                                string.IsNullOrEmpty(label) ? "[E] Собрать урожай" : label);
                        }
                        else
                        {
                            _nearestInteractable = null;
                            _nearestFallbackMatureCrop = crop;
                            CropDefinition cDef = BalanceDatabase.GetCrop(crop.CropType);
                            _nearestPromptLabel = cDef != null
                                ? $"[E] Собрать: {cDef.displayName}"
                                : "[E] Собрать урожай";
                        }

                        _nearestInteractableWorldPos = (Vector3)cellCenter + new Vector3(0f, 0.72f, 0f);
                    }
                }
            }

            bool hasTarget = (_nearestInteractable != null || _nearestFallbackMatureCrop != null)
                             && !string.IsNullOrEmpty(_nearestPromptLabel);

            if (!hasTarget)
            {
                if (_promptCanvasGo != null && _promptCanvasGo.activeSelf)
                {
                    _promptCanvasGo.SetActive(false);
                }
                return;
            }

            if (_promptCanvasGo != null)
            {
                if (!_promptCanvasGo.activeSelf)
                {
                    _promptCanvasGo.SetActive(true);
                }

                float floatBob = Mathf.Sin(Time.unscaledTime * 4.2f) * 0.055f;
                _promptCanvasGo.transform.position = _nearestInteractableWorldPos + new Vector3(0f, floatBob, -0.4f);
                if (_promptText != null && _promptText.text != _nearestPromptLabel)
                {
                    _promptText.text = _nearestPromptLabel;
                }
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                if (_nearestInteractable != null && _nearestInteractable.CanInteract())
                {
                    _nearestInteractable.Interact();
                }
                else if (_nearestFallbackMatureCrop != null && _nearestFallbackMatureCrop.IsMature)
                {
                    _nearestFallbackMatureCrop.Harvest();
                }
            }
        }

        private static string FormatPromptWithKeyPrefix(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            return raw.Contains("[E]") ? raw : $"[E] {raw}";
        }

        private Vector2 ComputeMouseWorldPosition()
        {
            Camera cam = Camera.main;
            if (cam == null && CameraFollowController.InstanceIfExists != null)
            {
                cam = CameraFollowController.InstanceIfExists.TargetCamera;
            }

            if (cam == null)
            {
                return (Vector2)transform.position + Vector2.down;
            }

            Vector3 mouseScreen = Input.mousePosition;
            mouseScreen.z = -cam.transform.position.z;
            Vector3 world = cam.ScreenToWorldPoint(mouseScreen);
            return new Vector2(world.x, world.y);
        }

        private void ResetDragMemory()
        {
            _lastDragSeedCell = new Vector2Int(-999, -999);
            _lastDragBuildCell = new Vector2Int(-999, -999);
            _lastDragDemolishCell = new Vector2Int(-999, -999);
        }

        // --- Procedural Pixel-Art Cursor & Range Sprites (Cached Once) ---

        private static Sprite GetOrCreateCursorFrameSprite()
        {
            if (_cachedCursorFrameSprite != null) return _cachedCursorFrameSprite;

            const int size = 128;
            Color[] px = new Color[size * size];
            Color interior = new Color(1f, 1f, 1f, 0.10f);
            Color thinEdge = new Color(1f, 1f, 1f, 0.35f);
            Color cornerBracket = Color.white;

            const int bracketLen = 28;
            const int bracketThick = 5;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool nearCornerX = x < bracketLen || x >= size - bracketLen;
                    bool nearCornerY = y < bracketLen || y >= size - bracketLen;
                    bool outerBand = x < bracketThick || x >= size - bracketThick || y < bracketThick || y >= size - bracketThick;
                    bool thinBand = x < 2 || x >= size - 2 || y < 2 || y >= size - 2;

                    if (outerBand && nearCornerX && nearCornerY)
                    {
                        px[y * size + x] = cornerBracket;
                    }
                    else if (thinBand)
                    {
                        px[y * size + x] = thinEdge;
                    }
                    else
                    {
                        px[y * size + x] = interior;
                    }
                }
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Generated_GridCursorFrame"
            };
            tex.SetPixels(px);
            tex.Apply(false, false);

            _cachedCursorFrameSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                128f,
                0u,
                SpriteMeshType.FullRect);
            return _cachedCursorFrameSprite;
        }

        private static Sprite GetOrCreateRangeRingSprite()
        {
            if (_cachedRangeRingSprite != null) return _cachedRangeRingSprite;

            const int size = 128;
            Color[] px = new Color[size * size];
            float half = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist > 1.0f) continue;

                    if (dist >= 0.94f)
                    {
                        px[y * size + x] = new Color(1f, 1f, 1f, 0.85f);
                    }
                    else
                    {
                        px[y * size + x] = new Color(1f, 1f, 1f, 0.14f);
                    }
                }
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "Generated_RangeRing"
            };
            tex.SetPixels(px);
            tex.Apply(false, false);

            _cachedRangeRingSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                128f,
                0u,
                SpriteMeshType.FullRect);
            return _cachedRangeRingSprite;
        }
    }
}
