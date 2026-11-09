using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using BioFarme.Data;
using BioFarme.Audio;

namespace BioFarme.Core
{
    /// <summary>
    /// Central game loop & state machine for "BioFarme: Сердце Колонии".
    /// Keeps Time.timeScale = 1f during TacticalPause so camera panning, UI, and building placement remain responsive
    /// while simulation systems tick via SimulationDeltaTime.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameManager : MonoBehaviour
    {
        private static GameManager _instance;
        private static bool _applicationQuitting;
        private static GameState _pendingBootState = GameState.MainMenu;
        private static bool _hasPendingBootOverride;

        public static GameManager InstanceIfExists => _instance;

        public static GameManager Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<GameManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[GameManager]");
                        _instance = go.AddComponent<GameManager>();
                    }
                }
                return _instance;
            }
        }

        [SerializeField] private GameState _initialState = GameState.MainMenu;

        public GameState State { get; private set; } = GameState.MainMenu;
        private GameState _stateBeforeOverlay = GameState.Playing;
        private bool _sessionHasRunActivity;
        private bool _sessionInitializedOnce;

        // Simulation Timing
        public float SimulationSpeed { get; set; } = 1f;
        public bool IsSimulationPaused => State != GameState.Playing;
        public bool IsTacticalPaused => State == GameState.TacticalPause;
        public bool IsGameplayInteractive => State == GameState.Playing || State == GameState.TacticalPause;
        public float SimulationDeltaTime => IsSimulationPaused ? 0f : (Time.deltaTime * Mathf.Max(0f, SimulationSpeed));

        // Active Mini-Game
        public MiniGameKind ActiveMiniGame { get; private set; } = MiniGameKind.GeneSplicing;

        // Colony Statistics for HUD & Victory/Defeat Summary
        public int WavesSurvived { get; private set; }
        public int CurrentWave { get; private set; } = 1;
        public float TotalBiomassHarvested { get; private set; }
        public int EnemiesKilled { get; private set; }
        public int StructuresBuilt { get; private set; }
        public int CropsHarvested { get; private set; }
        public int MiniGamesCompleted { get; private set; }
        public float MutagenSynthesized { get; private set; }
        public float ElapsedPlayTime { get; private set; }
        public float BonusAwakeningPercent { get; private set; }
        public string DefeatReason { get; private set; } = string.Empty;

        // Weather State
        public WeatherType CurrentWeather { get; private set; } = WeatherType.CalmSymbiosis;
        public float WeatherRemainingSeconds { get; private set; } = 50f;
        public WeatherDefinition CurrentWeatherDefinition => BalanceDatabase.GetWeather(CurrentWeather);

        // Emergency Heart Metabolism timer (prevents resource softlock)
        private float _emergencyMetabolismTimer;

        // Convenience pass-throughs to ResourceManager
        public NetworkPriorityMode CurrentPriority
        {
            get => ResourceManager.Instance != null ? ResourceManager.Instance.CurrentPriority : NetworkPriorityMode.Balanced;
            set
            {
                if (ResourceManager.Instance != null)
                {
                    ResourceManager.Instance.CurrentPriority = value;
                }
            }
        }

        public float CropGrowthMultiplier
        {
            get
            {
                float buff = ResourceManager.Instance != null ? ResourceManager.Instance.CropGrowthMultiplier : 1f;
                float weather = CurrentWeatherDefinition != null ? CurrentWeatherDefinition.cropGrowthMultiplier : 1f;
                return buff * weather;
            }
        }

        public float TurretFireRateMultiplier
            => ResourceManager.Instance != null ? ResourceManager.Instance.TurretFireRateMultiplier : 1f;

        public float BuffRemainingSeconds
            => ResourceManager.Instance != null ? ResourceManager.Instance.BuffRemainingSeconds : 0f;

        public float HeartAwakeningPercent
        {
            get
            {
                float mutagen = ResourceManager.InstanceIfExists != null
                    ? ResourceManager.InstanceIfExists.Get(ResourceType.Mutagen)
                    : 0f;
                float fromMutagen = (mutagen / BalanceDatabase.TargetMutagenForVictory) * 100f;
                return Mathf.Clamp(fromMutagen + BonusAwakeningPercent, 0f, 100f);
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            Time.timeScale = 1f;

            if (_hasPendingBootOverride)
            {
                _hasPendingBootOverride = false;
                State = _pendingBootState;
            }
            else
            {
                string activeSceneName = SceneManager.GetActiveScene().name;
                if (string.Equals(activeSceneName, "Gameplay", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(activeSceneName, "GameScene", StringComparison.OrdinalIgnoreCase))
                {
                    State = GameState.Playing;
                }
                else if (string.Equals(activeSceneName, "MainMenu", StringComparison.OrdinalIgnoreCase))
                {
                    State = GameState.MainMenu;
                }
                else
                {
                    State = _initialState;
                }
            }
        }

        private void Start()
        {
            // Ensure core singletons exist
            _ = ResourceManager.Instance;
            _ = AudioManager.Instance;
            _ = WorldPopupManager.Instance;

            if (State == GameState.Playing)
            {
                InitializeNewSessionInternal();
            }
            else
            {
                GameEvents.RaiseGameStateChanged(State, State);
            }
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
            float simDt = SimulationDeltaTime;
            if (simDt <= 0f) return;

            _sessionHasRunActivity = true;
            ElapsedPlayTime += simDt;

            TickWeather(simDt);
            TickEmergencyMetabolism(simDt);
        }

        private void TickWeather(float simDt)
        {
            WeatherRemainingSeconds -= simDt;
            if (WeatherRemainingSeconds <= 0f)
            {
                // Rotate cavern atmosphere organically
                WeatherType[] options =
                {
                    WeatherType.CalmSymbiosis,
                    WeatherType.AcidSporeRain,
                    WeatherType.BioDrought,
                    WeatherType.ParasiteSwarm
                };
                WeatherType next = options[UnityEngine.Random.Range(0, options.Length)];
                if (next == CurrentWeather)
                {
                    next = WeatherType.CalmSymbiosis;
                }
                SetWeather(next, UnityEngine.Random.Range(38f, 55f));
            }
        }

        private void TickEmergencyMetabolism(float simDt)
        {
            if (ResourceManager.InstanceIfExists == null) return;
            float bio = ResourceManager.InstanceIfExists.GetRaw(ResourceType.Biomass);
            float spores = ResourceManager.InstanceIfExists.GetRaw(ResourceType.Spores);
            if (bio < 15f && spores < 6f)
            {
                _emergencyMetabolismTimer += simDt;
                if (_emergencyMetabolismTimer >= 2.5f)
                {
                    _emergencyMetabolismTimer = 0f;
                    ResourceManager.InstanceIfExists.Add(ResourceType.Biomass, 2f);
                }
            }
            else
            {
                _emergencyMetabolismTimer = 0f;
            }
        }

        public void SetWeather(WeatherType weather, float durationSec = 45f)
        {
            CurrentWeather = weather;
            WeatherRemainingSeconds = Mathf.Max(5f, durationSec);
            GameEvents.RaiseWeatherChanged(CurrentWeather, WeatherRemainingSeconds);

            WeatherDefinition def = BalanceDatabase.GetWeather(weather);
            if (def != null && State == GameState.Playing)
            {
                GameEvents.RaiseBannerToast($"Атмосфера пещеры: {def.displayName}", def.ambientTint);
            }
        }

        // --- Core State Transitions ---

        public void SetState(GameState newState)
        {
            if (State == newState) return;
            GameState prev = State;
            State = newState;
            Time.timeScale = 1f;

            if (State != GameState.Playing && AudioManager.Instance != null)
            {
                AudioManager.Instance.StopSprayLoop();
            }

            if (prev == GameState.TacticalPause || newState == GameState.TacticalPause)
            {
                GameEvents.RaiseTacticalPauseChanged(newState == GameState.TacticalPause);
            }

            GameEvents.RaiseGameStateChanged(prev, newState);
        }

        public void StartNewGame()
        {
            Time.timeScale = 1f;

            string activeSceneName = SceneManager.GetActiveScene().name;
            string targetGameplayScene = null;
            if (Application.CanStreamedLevelBeLoaded("Gameplay"))
            {
                targetGameplayScene = "Gameplay";
            }
            else if (Application.CanStreamedLevelBeLoaded("GameScene"))
            {
                targetGameplayScene = "GameScene";
            }

            // If a separate "Gameplay" (or "GameScene") exists in build settings and we're not in it, load it
            if (!string.IsNullOrEmpty(targetGameplayScene) &&
                !string.Equals(activeSceneName, targetGameplayScene, StringComparison.OrdinalIgnoreCase))
            {
                WorldEntityRegistry.ClearAll();
                _pendingBootState = GameState.Playing;
                _hasPendingBootOverride = true;
                SceneManager.LoadScene(targetGameplayScene);
                return;
            }

            // If a session already spawned world entities, reload active scene cleanly for a fresh start
            if (_sessionHasRunActivity || _sessionInitializedOnce || State == GameState.Victory || State == GameState.Defeat)
            {
                WorldEntityRegistry.ClearAll();
                _pendingBootState = GameState.Playing;
                _hasPendingBootOverride = true;
                Scene activeScene = SceneManager.GetActiveScene();
                if (activeScene.buildIndex >= 0)
                {
                    SceneManager.LoadScene(activeScene.buildIndex);
                }
                else
                {
                    SceneManager.LoadScene(activeScene.name);
                }
                return;
            }

            InitializeNewSessionInternal();
        }

        private void InitializeNewSessionInternal()
        {
            ResetStatistics();
            _sessionHasRunActivity = false;

            if (ResourceManager.Instance != null)
            {
                ResourceManager.Instance.ResetToStartingResources(notifyEvents: true);
            }

            if (Grid.BioGridManager.InstanceIfExists != null)
            {
                if (_sessionInitializedOnce)
                {
                    Grid.BioGridManager.InstanceIfExists.RegenerateCavernMap();
                }
                else
                {
                    Grid.BioGridManager.InstanceIfExists.EnsureMapGenerated();
                }
            }

            _sessionInitializedOnce = true;

            CurrentWeather = WeatherType.CalmSymbiosis;
            WeatherRemainingSeconds = 50f;

            GameState prev = State;
            State = GameState.Playing;
            Time.timeScale = 1f;

            GameEvents.RaiseNewGameSessionInitialized();
            GameEvents.RaiseWeatherChanged(CurrentWeather, WeatherRemainingSeconds);
            GameEvents.RaiseGameStateChanged(prev, GameState.Playing);
        }

        public void LoadMainMenu()
        {
            Time.timeScale = 1f;
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.StopSprayLoop();
            }

            WorldEntityRegistry.ClearAll();

            if (Application.CanStreamedLevelBeLoaded("MainMenu"))
            {
                _pendingBootState = GameState.MainMenu;
                _hasPendingBootOverride = true;
                SceneManager.LoadScene("MainMenu");
                return;
            }

            _pendingBootState = GameState.MainMenu;
            _hasPendingBootOverride = true;
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.buildIndex >= 0)
            {
                SceneManager.LoadScene(activeScene.buildIndex);
            }
            else
            {
                SceneManager.LoadScene(activeScene.name);
            }
        }

        public void ToggleTacticalPause()
        {
            if (State == GameState.Playing)
            {
                SetState(GameState.TacticalPause);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(AudioKeys.SfxUiClick, 0.7f, 0.02f);
            }
            else if (State == GameState.TacticalPause)
            {
                SetState(GameState.Playing);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(AudioKeys.SfxUiClick, 0.7f, 0.02f);
            }
        }

        public void TogglePauseMenu()
        {
            if (State == GameState.Playing || State == GameState.TacticalPause)
            {
                _stateBeforeOverlay = State;
                SetState(GameState.Paused);
            }
            else if (State == GameState.Paused)
            {
                SetState(_stateBeforeOverlay == GameState.TacticalPause ? GameState.TacticalPause : GameState.Playing);
            }
        }

        public void ResumeFromPause()
        {
            if (State == GameState.Paused)
            {
                SetState(_stateBeforeOverlay == GameState.TacticalPause ? GameState.TacticalPause : GameState.Playing);
            }
            else if (State == GameState.TacticalPause)
            {
                SetState(GameState.Playing);
            }
        }

        public void OpenMiniGame(MiniGameKind kind)
        {
            if (State == GameState.Victory || State == GameState.Defeat || State == GameState.MainMenu)
            {
                return;
            }

            if (State == GameState.Playing || State == GameState.TacticalPause)
            {
                _stateBeforeOverlay = State;
            }
            else
            {
                _stateBeforeOverlay = GameState.Playing;
            }

            ActiveMiniGame = kind;
            SetState(GameState.MiniGame);
            GameEvents.RaiseMiniGameRequested(kind);
        }

        public void CompleteMiniGame(MiniGameKind kind, bool success, int scoreTier = 1)
        {
            if (success)
            {
                MiniGamesCompleted++;
                int effectiveTier = Mathf.Max(1, scoreTier);
                if (ResourceManager.Instance != null)
                {
                    if (kind == MiniGameKind.GeneSplicing)
                    {
                        ResourceManager.Instance.TriggerGeneSpliceReward(effectiveTier);
                    }
                    else
                    {
                        ResourceManager.Instance.TriggerCoreDefibReward(effectiveTier);
                    }
                }
            }
            else
            {
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlaySfx(AudioKeys.SfxMiniGameFail, 0.8f, 0.03f);
                }
                GameEvents.RaiseBannerToast("Синтез прерван! Попробуйте снова.", new Color(1f, 0.45f, 0.4f));
            }

            GameEvents.RaiseMiniGameCompleted(kind, success, scoreTier);

            if (State == GameState.MiniGame)
            {
                SetState(GameState.Playing);
            }

            CheckVictoryCondition();
        }

        public void CancelMiniGame()
        {
            if (State == GameState.MiniGame)
            {
                SetState(_stateBeforeOverlay == GameState.TacticalPause ? GameState.TacticalPause : GameState.Playing);
            }
        }

        public void CheckVictoryCondition()
        {
            if (State == GameState.Victory || State == GameState.Defeat || State == GameState.MainMenu)
            {
                return;
            }

            bool allWavesCleared = WavesSurvived >= BalanceDatabase.TotalWaves;
            float mutagen = ResourceManager.InstanceIfExists != null
                ? ResourceManager.InstanceIfExists.Get(ResourceType.Mutagen)
                : 0f;
            bool mutagenReady = mutagen >= BalanceDatabase.TargetMutagenForVictory || HeartAwakeningPercent >= 99.9f;

            if (allWavesCleared && mutagenReady)
            {
                TriggerVictory();
            }
        }

        /// <summary>
        /// Allows manual or Heart Core interaction awakening once 100% Awakening (>= 10 Mutagen) is achieved!
        /// </summary>
        public bool TryAwakenHeartCore()
        {
            if (HeartAwakeningPercent >= 99.9f)
            {
                TriggerVictory();
                return true;
            }
            return false;
        }

        public void TriggerVictory()
        {
            if (State == GameState.Victory || State == GameState.Defeat) return;

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(AudioKeys.SfxVictory, 1.0f, 0.0f);
            }

            SetState(GameState.Victory);
        }

        public void TriggerDefeat(string reason = "Живое Сердце Колонии уничтожено роем!")
        {
            if (State == GameState.Victory || State == GameState.Defeat) return;

            DefeatReason = string.IsNullOrWhiteSpace(reason)
                ? "Живое Сердце Колонии уничтожено роем!"
                : reason;

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(AudioKeys.SfxDefeat, 1.0f, 0.0f);
            }

            SetState(GameState.Defeat);
        }

        // --- Stat Recording Helpers ---

        public void ResetStatistics()
        {
            WavesSurvived = 0;
            CurrentWave = 1;
            TotalBiomassHarvested = 0f;
            EnemiesKilled = 0;
            StructuresBuilt = 0;
            CropsHarvested = 0;
            MiniGamesCompleted = 0;
            MutagenSynthesized = 0f;
            ElapsedPlayTime = 0f;
            BonusAwakeningPercent = 0f;
            DefeatReason = string.Empty;
        }

        public void SetCurrentWave(int waveNumber)
        {
            CurrentWave = Mathf.Clamp(waveNumber, 1, BalanceDatabase.TotalWaves);
        }

        public void RecordWaveSurvived(int waveNumber)
        {
            WavesSurvived = Mathf.Max(WavesSurvived, waveNumber);
            GameEvents.RaiseWaveCleared(waveNumber);

            WaveDefinition waveDef = BalanceDatabase.GetWave(waveNumber);
            if (waveDef != null && waveDef.completionRewards != null && ResourceManager.Instance != null)
            {
                for (int i = 0; i < waveDef.completionRewards.Length; i++)
                {
                    ResourceCost r = waveDef.completionRewards[i];
                    ResourceManager.Instance.Add(r.resource, r.amount);
                }
            }

            CheckVictoryCondition();

            if (State != GameState.Victory && WavesSurvived >= BalanceDatabase.TotalWaves)
            {
                float need = Mathf.Max(0f, BalanceDatabase.TargetMutagenForVictory -
                    (ResourceManager.Instance != null ? ResourceManager.Instance.Get(ResourceType.Mutagen) : 0f));
                if (need > 0f)
                {
                    GameEvents.RaiseBannerToast(
                        $"Все 5 волн отбиты! Синтезируйте ещё {need:0.#} ед. Мутагена для пробуждения Сердца!",
                        BalanceDatabase.GetResourceColor(ResourceType.Mutagen));
                }
            }
        }

        public void RecordBiomassHarvested(float amount)
        {
            if (amount > 0f) TotalBiomassHarvested += amount;
        }

        public void RecordEnemyKilled(EnemyType enemyType, Vector2 worldPos = default)
        {
            EnemiesKilled++;
            GameEvents.RaiseEnemyKilled(enemyType, worldPos);
        }

        public void RecordStructureBuilt()
        {
            StructuresBuilt++;
        }

        public void RecordCropHarvested()
        {
            CropsHarvested++;
        }

        public void RecordMutagenSynthesized(float amount)
        {
            if (amount > 0f) MutagenSynthesized += amount;
        }

        public void AddBonusAwakeningPercent(float percentDelta)
        {
            if (percentDelta <= 0f) return;
            BonusAwakeningPercent = Mathf.Clamp(BonusAwakeningPercent + percentDelta, 0f, 100f);
            float mut = ResourceManager.InstanceIfExists != null ? ResourceManager.InstanceIfExists.Get(ResourceType.Mutagen) : 0f;
            GameEvents.RaiseCoreAwakeningChanged(HeartAwakeningPercent / 100f, mut, BalanceDatabase.TargetMutagenForVictory);
            CheckVictoryCondition();
        }
    }
}
