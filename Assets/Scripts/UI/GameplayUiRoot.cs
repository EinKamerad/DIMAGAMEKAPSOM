using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.MiniGames;

namespace BioFarme.UI
{
    /// <summary>
    /// Master UI coordinator MonoBehaviour for "BioFarme: Сердце Колонии".
    /// Instantiates and wires GameplayHudUI (with MinimapUI), TacticalOverlayUI, BioCodexWindowUI,
    /// BioMiniGameController, PauseMenuUI, EndScreensUI, and BiopunkTooltipPopup under a single 1920x1080 Canvas.
    /// Runs at execution order 200 so hotkey toggles never conflict if a world/player script already changed GameState this frame.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class GameplayUiRoot : MonoBehaviour
    {
        private const float GeneSplicingCooldownDuration = 25f;
        private const float CoreDefibCooldownDuration = 28f;

        private static GameplayUiRoot _instance;
        public static GameplayUiRoot Instance => _instance;

        private Canvas _canvas;
        private GameplayHudUI _hud;
        private TacticalOverlayUI _tacticalOverlay;
        private BioCodexWindowUI _bioCodex;
        private BioMiniGameController _miniGameController;
        private PauseMenuUI _pauseMenu;
        private EndScreensUI _endScreens;

        private float _geneSplicingCooldown;
        private float _coreDefibCooldown;
        private int _lastStateChangeFrame = -1;
        private bool _built;

        public GameplayHudUI Hud => _hud;
        public BioCodexWindowUI BioCodex => _bioCodex;
        public BioMiniGameController MiniGames => _miniGameController;
        public PauseMenuUI PauseMenu => _pauseMenu;
        public EndScreensUI EndScreens => _endScreens;

        public float GeneSplicingCooldown => _geneSplicingCooldown;
        public float CoreDefibCooldown => _coreDefibCooldown;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            BuildAllScreensIfNeeded();
        }

        private void OnEnable()
        {
            GameEvents.OnGameStateChanged += HandleGameStateChanged;
            GameEvents.OnMiniGameCompleted += HandleMiniGameCompleted;
            GameEvents.OnNewGameSessionInitialized += HandleNewSessionInitialized;
        }

        private void OnDisable()
        {
            GameEvents.OnGameStateChanged -= HandleGameStateChanged;
            GameEvents.OnMiniGameCompleted -= HandleMiniGameCompleted;
            GameEvents.OnNewGameSessionInitialized -= HandleNewSessionInitialized;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Start()
        {
            GameState current = GameManager.Instance != null ? GameManager.Instance.State : GameState.Playing;
            ApplyGameStateVisibility(current);

            if (current == GameState.MainMenu && FindFirstObjectByType<MainMenuUI>() == null)
            {
                GameObject menuGo = new GameObject("[MainMenuUI]");
                menuGo.AddComponent<MainMenuUI>();
            }
        }

        private void BuildAllScreensIfNeeded()
        {
            if (_built) return;
            _built = true;

            _canvas = GetComponentInChildren<Canvas>(true);
            if (_canvas == null)
            {
                _canvas = BiopunkUiBuilder.CreateScreenCanvas("GameplayCanvas_1920x1080", transform, sortingOrder: 100);
            }
            else
            {
                BiopunkUiBuilder.EnsureEventSystem();
            }

            // 1. Gameplay HUD + Real-Time Minimap + Toasts
            GameObject hudHost = new GameObject("HudController");
            hudHost.transform.SetParent(_canvas.transform, false);
            _hud = hudHost.AddComponent<GameplayHudUI>();
            _hud.Initialize(this, _canvas.transform);

            // 2. Tactical Pause Overlay ([Space])
            GameObject tacHost = new GameObject("TacticalOverlayController");
            tacHost.transform.SetParent(_canvas.transform, false);
            _tacticalOverlay = tacHost.AddComponent<TacticalOverlayUI>();
            _tacticalOverlay.Build(_canvas.transform);

            // 3. Bio-Codex Window ([B] / [Tab])
            GameObject codexHost = new GameObject("BioCodexController");
            codexHost.transform.SetParent(_canvas.transform, false);
            _bioCodex = codexHost.AddComponent<BioCodexWindowUI>();
            _bioCodex.Build(_canvas.transform);

            // 4. Interactive Mini-Game Screen ([M] / [H] / [E])
            GameObject mgHost = new GameObject("BioMiniGameController");
            mgHost.transform.SetParent(_canvas.transform, false);
            _miniGameController = mgHost.AddComponent<BioMiniGameController>();
            _miniGameController.Build(_canvas.transform);

            // 5. Pause & Controls Help Menu ([Esc] / [F1])
            GameObject pauseHost = new GameObject("PauseMenuController");
            pauseHost.transform.SetParent(_canvas.transform, false);
            _pauseMenu = pauseHost.AddComponent<PauseMenuUI>();
            _pauseMenu.Build(_canvas.transform);

            // 6. Victory & Defeat End Screens
            GameObject endHost = new GameObject("EndScreensController");
            endHost.transform.SetParent(_canvas.transform, false);
            _endScreens = endHost.AddComponent<EndScreensUI>();
            _endScreens.Build(_canvas.transform);

            // 7. Topmost Floating Tooltip Layer
            BiopunkTooltipPopup.AttachToCanvas(_canvas);
        }

        private void Update()
        {
            GameManager gm = GameManager.InstanceIfExists;
            if (gm == null) return;

            // Tick mini-game cooldowns during active gameplay
            float simDt = gm.SimulationDeltaTime;
            if (simDt > 0f)
            {
                if (_geneSplicingCooldown > 0f)
                {
                    _geneSplicingCooldown = Mathf.Max(0f, _geneSplicingCooldown - simDt);
                }
                if (_coreDefibCooldown > 0f)
                {
                    _coreDefibCooldown = Mathf.Max(0f, _coreDefibCooldown - simDt);
                }
            }

            GameState state = gm.State;
            if (state == GameState.MainMenu || state == GameState.Victory || state == GameState.Defeat)
            {
                return;
            }

            HandleHotkeys(gm, state);
        }

        private void HandleHotkeys(GameManager gm, GameState state)
        {
            // [Esc] Hierarchy: MiniGame -> BioCodex -> PauseMenu
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_lastStateChangeFrame == Time.frameCount) return;

                if (state == GameState.MiniGame)
                {
                    BiopunkUiBuilder.PlayClickSfx();
                    gm.CancelMiniGame();
                    return;
                }

                if (_bioCodex != null && _bioCodex.IsOpen)
                {
                    BiopunkUiBuilder.PlayClickSfx();
                    _bioCodex.Close();
                    return;
                }

                BiopunkUiBuilder.PlayClickSfx();
                gm.TogglePauseMenu();
                return;
            }

            // [F1] Direct Pause / Controls Reference Toggle
            if (Input.GetKeyDown(KeyCode.F1))
            {
                if (_lastStateChangeFrame == Time.frameCount) return;
                if (state == GameState.MiniGame) return;

                if (_bioCodex != null && _bioCodex.IsOpen)
                {
                    _bioCodex.Close();
                }

                BiopunkUiBuilder.PlayClickSfx();
                gm.TogglePauseMenu();
                return;
            }

            // Ignore gameplay window hotkeys while inside MiniGame or PauseMenu
            if (state == GameState.MiniGame || state == GameState.Paused)
            {
                return;
            }

            // [B] or [Tab] Toggle Bio-Codex Window
            if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.Tab))
            {
                BiopunkUiBuilder.PlayClickSfx();
                ToggleBioCodex();
                return;
            }

            // [N] Collapse / Restore Minimap
            if (Input.GetKeyDown(KeyCode.N) && _hud != null && _hud.Minimap != null)
            {
                BiopunkUiBuilder.PlayClickSfx();
                _hud.Minimap.ToggleCollapsed();
                return;
            }

            // [M] Trigger Gene-Splicing Mini-Game
            if (Input.GetKeyDown(KeyCode.M))
            {
                if (_lastStateChangeFrame == Time.frameCount) return;
                RequestGeneSplicingMiniGame();
                return;
            }

            // [H] Trigger Core Defibrillation Mini-Game
            if (Input.GetKeyDown(KeyCode.H))
            {
                if (_lastStateChangeFrame == Time.frameCount) return;
                RequestCoreDefibMiniGame();
                return;
            }

            // [Space] Toggle Tactical Pause (guarded against double-toggle if another script handled Space first)
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (_lastStateChangeFrame == Time.frameCount) return;
                if (_bioCodex != null && _bioCodex.IsOpen) return;

                gm.ToggleTacticalPause();
            }
        }

        // =====================================================================
        // PUBLIC UI COMMANDS (Wired to HUD Buttons & Hotkeys)
        // =====================================================================

        public void ToggleBioCodex()
        {
            if (_bioCodex == null) return;
            GameManager gm = GameManager.InstanceIfExists;
            if (gm != null && (gm.State == GameState.MiniGame || gm.State == GameState.Paused))
            {
                return;
            }
            _bioCodex.Toggle();
        }

        public void TogglePauseMenu()
        {
            if (_bioCodex != null && _bioCodex.IsOpen)
            {
                _bioCodex.Close();
            }
            GameManager.Instance?.TogglePauseMenu();
        }

        public void RequestGeneSplicingMiniGame()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || !gm.IsGameplayInteractive) return;

            if (_geneSplicingCooldown > 0.05f)
            {
                GameEvents.RaiseBannerToast(
                    $"Камера Био-Синтеза перезаряжается: ещё {Mathf.CeilToInt(_geneSplicingCooldown)}с",
                    BiopunkUiBuilder.AccentAmber);
                return;
            }

            if (_bioCodex != null && _bioCodex.IsOpen) _bioCodex.Close();
            BiopunkUiBuilder.PlayClickSfx();
            gm.OpenMiniGame(MiniGameKind.GeneSplicing);
        }

        public void RequestCoreDefibMiniGame()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || !gm.IsGameplayInteractive) return;

            if (_coreDefibCooldown > 0.05f)
            {
                GameEvents.RaiseBannerToast(
                    $"Конденсаторы Дефибрилляции заряжаются: ещё {Mathf.CeilToInt(_coreDefibCooldown)}с",
                    BiopunkUiBuilder.AccentAmber);
                return;
            }

            if (_bioCodex != null && _bioCodex.IsOpen) _bioCodex.Close();
            BiopunkUiBuilder.PlayClickSfx();
            gm.OpenMiniGame(MiniGameKind.CoreDefibrillation);
        }

        // =====================================================================
        // STATE SYNCHRONIZATION
        // =====================================================================

        private void HandleNewSessionInitialized()
        {
            _geneSplicingCooldown = 0f;
            _coreDefibCooldown = 0f;
            if (_bioCodex != null) _bioCodex.Close();
        }

        private void HandleMiniGameCompleted(MiniGameKind kind, bool success, int scoreTier)
        {
            if (!success) return;

            if (kind == MiniGameKind.GeneSplicing)
            {
                _geneSplicingCooldown = GeneSplicingCooldownDuration;
            }
            else if (kind == MiniGameKind.CoreDefibrillation)
            {
                _coreDefibCooldown = CoreDefibCooldownDuration;
            }
        }

        private void HandleGameStateChanged(GameState prev, GameState next)
        {
            _lastStateChangeFrame = Time.frameCount;
            ApplyGameStateVisibility(next);
        }

        private void ApplyGameStateVisibility(GameState state)
        {
            BuildAllScreensIfNeeded();

            bool showHud = state != GameState.MainMenu && state != GameState.Victory && state != GameState.Defeat;
            if (_hud != null) _hud.SetVisible(showHud);

            if (state == GameState.MiniGame || state == GameState.Victory || state == GameState.Defeat || state == GameState.MainMenu)
            {
                if (_bioCodex != null && _bioCodex.IsOpen)
                {
                    _bioCodex.Close();
                }
            }

            if (_tacticalOverlay != null) _tacticalOverlay.SyncVisibility();
            if (_pauseMenu != null) _pauseMenu.SyncWithState(state);
            if (_endScreens != null) _endScreens.SyncWithState(state);
        }
    }
}
