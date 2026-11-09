using System.Collections.Generic;
using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;
using BioFarme.Audio;
using BioFarme.Factory;

namespace BioFarme.Combat
{
    /// <summary>
    /// Orchestrates the 5 escalating subterranean insect waves + Wave 5 Brood Alpha Boss!
    /// Advances preparation countdowns and multi-burrow spawn streams using GameManager.SimulationDeltaTime,
    /// rewards early wave calls (+35 Biomass), and verifies Wave 5 + 10 Mutagen Heart Awakening victory!
    /// </summary>
    public sealed class WaveDirector : MonoBehaviour
    {
        private const float EarlyWaveBiomassBonus = 35f;

        private enum DirectorPhase
        {
            PreparingWave = 0,
            ActiveAssault = 1,
            AllWavesCleared = 2
        }

        private sealed class ActiveSpawnStream
        {
            public WaveSpawnEntry Entry;
            public int SpawnedSoFar;
            public float NextSpawnTime;
            public int BurrowCursor;

            public bool IsFinished => Entry == null || SpawnedSoFar >= Entry.count;
        }

        private static WaveDirector _instance;
        private static bool _applicationQuitting;

        public static WaveDirector InstanceIfExists => _instance;

        public static WaveDirector Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<WaveDirector>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[WaveDirector]");
                        _instance = go.AddComponent<WaveDirector>();
                    }
                }
                return _instance;
            }
        }

        private Transform _enemiesRoot;
        private DirectorPhase _phase = DirectorPhase.PreparingWave;
        private int _currentWaveNumber = 1;
        private float _prepCountdownRemaining = 45f;
        private float _countdownBroadcastAccum;
        private float _waveElapsedSec;
        private int _globalRoundRobinBurrow;

        private readonly List<ActiveSpawnStream> _spawnStreams = new List<ActiveSpawnStream>(8);

        public int CurrentWaveNumber => _currentWaveNumber;
        public float PrepCountdownRemaining => _phase == DirectorPhase.PreparingWave ? _prepCountdownRemaining : 0f;
        public bool IsWaveInProgress => _phase == DirectorPhase.ActiveAssault;
        public bool AreAllWavesCleared => _phase == DirectorPhase.AllWavesCleared;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            EnsureEnemiesRoot();
        }

        private void OnEnable()
        {
            GameEvents.OnNewGameSessionInitialized += HandleNewGameSessionInitialized;
            GameEvents.OnSendNextWaveEarlyRequested += HandleSendNextWaveEarlyRequested;
            GameEvents.OnResourceChanged += HandleResourceChangedForPostWave5Victory;
        }

        private void Start()
        {
            ResetToWaveOne();
        }

        private void OnDisable()
        {
            GameEvents.OnNewGameSessionInitialized -= HandleNewGameSessionInitialized;
            GameEvents.OnSendNextWaveEarlyRequested -= HandleSendNextWaveEarlyRequested;
            GameEvents.OnResourceChanged -= HandleResourceChangedForPostWave5Victory;
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

        private void EnsureEnemiesRoot()
        {
            if (_enemiesRoot != null) return;
            GameObject rootGo = new GameObject("ActiveHiveSwarm");
            rootGo.transform.SetParent(transform, false);
            _enemiesRoot = rootGo.transform;
        }

        private void HandleNewGameSessionInitialized()
        {
            ClearAllSpawnedEnemies();
            ResetToWaveOne();
        }

        public void ClearAllSpawnedEnemies()
        {
            _spawnStreams.Clear();
            if (_enemiesRoot != null)
            {
                Destroy(_enemiesRoot.gameObject);
                _enemiesRoot = null;
            }
            EnsureEnemiesRoot();
            WorldEntityRegistry.PruneDeadEnemies();
        }

        public void ResetToWaveOne()
        {
            _currentWaveNumber = 1;
            _phase = DirectorPhase.PreparingWave;
            _spawnStreams.Clear();

            WaveDefinition wave1 = BalanceDatabase.GetWave(1);
            _prepCountdownRemaining = wave1 != null && wave1.prepDurationSec > 5f ? wave1.prepDurationSec : 45f;
            _countdownBroadcastAccum = 0f;

            GameManager.InstanceIfExists?.SetCurrentWave(1);
            GameEvents.RaiseWaveCountdownTick(_currentWaveNumber, _prepCountdownRemaining);
        }

        private void Update()
        {
            float simDt = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.SimulationDeltaTime
                : Time.deltaTime;

            if (simDt <= 0f) return;

            switch (_phase)
            {
                case DirectorPhase.PreparingWave:
                    TickPreparationPhase(simDt);
                    break;

                case DirectorPhase.ActiveAssault:
                    TickActiveAssaultPhase(simDt);
                    break;

                case DirectorPhase.AllWavesCleared:
                    // Continuously ensure Victory triggers the instant Mutagen reaches >= 10!
                    GameManager.InstanceIfExists?.CheckVictoryCondition();
                    break;
            }
        }

        private void TickPreparationPhase(float simDt)
        {
            _prepCountdownRemaining = Mathf.Max(0f, _prepCountdownRemaining - simDt);
            _countdownBroadcastAccum += simDt;

            if (_countdownBroadcastAccum >= 0.20f || _prepCountdownRemaining <= 0f)
            {
                _countdownBroadcastAccum = 0f;
                GameEvents.RaiseWaveCountdownTick(_currentWaveNumber, _prepCountdownRemaining);
            }

            if (_prepCountdownRemaining <= 0f)
            {
                StartWaveNow();
            }
        }

        private void HandleSendNextWaveEarlyRequested()
        {
            if (_phase != DirectorPhase.PreparingWave || _prepCountdownRemaining <= 0.1f)
            {
                return;
            }

            if (GameManager.InstanceIfExists != null && !GameManager.InstanceIfExists.IsGameplayInteractive)
            {
                return;
            }

            // Award +35 Biomass courage bonus!
            ResourceManager.Instance?.Add(ResourceType.Biomass, EarlyWaveBiomassBonus);

            Vector2 popupPos = CoreHeartEntity.InstanceIfExists != null
                ? CoreHeartEntity.InstanceIfExists.WorldPosition + Vector2.up * 0.8f
                : new Vector2(18.5f, 13.3f);

            WorldPopupManager.SpawnResourceGain(popupPos, ResourceType.Biomass, EarlyWaveBiomassBonus);
            GameEvents.RaiseBannerToast(
                $"Волна {_currentWaveNumber} вызвана досрочно! Бонус: +35 Биомассы!",
                BalanceDatabase.GetResourceColor(ResourceType.Biomass));

            _prepCountdownRemaining = 0f;
            GameEvents.RaiseWaveCountdownTick(_currentWaveNumber, 0f);

            // If player clicked early wave while in TacticalPause, unpause so the wave marches immediately
            if (GameManager.InstanceIfExists != null && GameManager.InstanceIfExists.IsTacticalPaused)
            {
                GameManager.InstanceIfExists.ResumeFromPause();
            }

            StartWaveNow();
        }

        private void StartWaveNow()
        {
            WaveDefinition waveDef = BalanceDatabase.GetWave(_currentWaveNumber);
            if (waveDef == null) return;

            _phase = DirectorPhase.ActiveAssault;
            _waveElapsedSec = 0f;
            _spawnStreams.Clear();

            GameManager.InstanceIfExists?.SetCurrentWave(_currentWaveNumber);

            if (GameManager.InstanceIfExists != null && GameManager.InstanceIfExists.CurrentWeather != waveDef.waveWeather)
            {
                GameManager.InstanceIfExists.SetWeather(waveDef.waveWeather, 52f);
            }

            if (waveDef.entries != null)
            {
                for (int i = 0; i < waveDef.entries.Length; i++)
                {
                    WaveSpawnEntry entry = waveDef.entries[i];
                    if (entry == null || entry.count <= 0) continue;

                    _spawnStreams.Add(new ActiveSpawnStream
                    {
                        Entry = entry,
                        SpawnedSoFar = 0,
                        NextSpawnTime = Mathf.Max(0f, entry.startDelaySec),
                        BurrowCursor = entry.preferredBurrowIndex
                    });
                }
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(AudioKeys.SfxWaveAlert, 0.92f, 0.02f);
            }

            GameEvents.RaiseWaveStarted(_currentWaveNumber, waveDef);
            GameEvents.RaiseBannerToast(
                $"{waveDef.displayName}: {waveDef.warningSubtitle}",
                new Color(1.0f, 0.40f, 0.30f, 1f));
        }

        private void TickActiveAssaultPhase(float simDt)
        {
            _waveElapsedSec += simDt;
            bool allStreamsDone = true;

            for (int i = 0; i < _spawnStreams.Count; i++)
            {
                ActiveSpawnStream stream = _spawnStreams[i];
                if (stream.IsFinished) continue;

                allStreamsDone = false;
                while (!stream.IsFinished && _waveElapsedSec >= stream.NextSpawnTime)
                {
                    SpawnEnemyFromStream(stream);
                    stream.SpawnedSoFar++;
                    stream.NextSpawnTime += Mathf.Max(0.20f, stream.Entry.spawnInterval);
                }

                if (!stream.IsFinished)
                {
                    allStreamsDone = false;
                }
            }

            if (!allStreamsDone) return;

            WorldEntityRegistry.PruneDeadEnemies();
            if (WorldEntityRegistry.ActiveEnemies.Count == 0)
            {
                CompleteCurrentWave();
            }
        }

        private void SpawnEnemyFromStream(ActiveSpawnStream stream)
        {
            BioGridManager grid = BioGridManager.Instance;
            int burrowCount = grid != null ? Mathf.Max(1, grid.BurrowCount) : 4;

            int burrowIdx;
            if (stream.Entry.preferredBurrowIndex >= 0)
            {
                burrowIdx = stream.Entry.preferredBurrowIndex % burrowCount;
            }
            else
            {
                burrowIdx = _globalRoundRobinBurrow % burrowCount;
                _globalRoundRobinBurrow++;
            }

            Vector2 burrowWorld = grid != null
                ? (Vector2)grid.GetBurrowSpawnWorldPosition(burrowIdx)
                : new Vector2(1.5f, 12.5f);

            Vector2 jitter = Random.insideUnitCircle * 0.24f;
            Vector2 spawnPos = burrowWorld + jitter;

            SpawnEnemy(stream.Entry.enemyType, spawnPos);

            if (stream.Entry.enemyType == EnemyType.BroodAlphaBoss)
            {
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlaySfx(AudioKeys.SfxWaveAlert, 1.0f, 0.0f);
                }
                GameEvents.RaiseBannerToast(
                    "АЛЬФА-МАТКА УЛЬЯ ВЫШЛА НА ОХОТУ! ЗАЩИТИТЕ ЖИВОЕ СЕРДЦЕ!",
                    new Color(0.98f, 0.25f, 0.72f, 1f));
            }
        }

        public EnemyAgent SpawnEnemy(EnemyType type, Vector2 worldPos)
        {
            EnsureEnemiesRoot();

            GameObject go = new GameObject($"Enemy_{type}");
            go.transform.SetParent(_enemiesRoot, false);

            EnemyAgent agent = go.AddComponent<EnemyAgent>();
            agent.Initialize(type, worldPos);

            if (ProjectileManager.InstanceIfExists != null)
            {
                ProjectileManager.InstanceIfExists.SpawnWorldRingVfx(
                    worldPos,
                    type == EnemyType.BroodAlphaBoss ? 1.6f : 0.55f,
                    new Color(0.88f, 0.30f, 0.45f, 0.65f),
                    0.26f);
            }

            return agent;
        }

        private void CompleteCurrentWave()
        {
            int clearedWave = _currentWaveNumber;
            WaveDefinition waveDef = BalanceDatabase.GetWave(clearedWave);

            // Visual popups above Heart Core for wave completion rewards
            Vector2 rewardOrigin = CoreHeartEntity.InstanceIfExists != null
                ? CoreHeartEntity.InstanceIfExists.WorldPosition + Vector2.up * 0.7f
                : new Vector2(18.5f, 13.2f);

            if (waveDef != null && waveDef.completionRewards != null)
            {
                for (int i = 0; i < waveDef.completionRewards.Length; i++)
                {
                    ResourceCost r = waveDef.completionRewards[i];
                    Vector2 offset = new Vector2(0f, i * 0.32f);
                    WorldPopupManager.SpawnResourceGain(rewardOrigin + offset, r.resource, r.amount);
                }
            }

            // RecordWaveSurvived grants completionRewards, raises OnWaveCleared, and calls CheckVictoryCondition!
            if (GameManager.Instance != null)
            {
                GameManager.Instance.RecordWaveSurvived(clearedWave);
            }
            else
            {
                if (waveDef != null && waveDef.completionRewards != null && ResourceManager.Instance != null)
                {
                    for (int i = 0; i < waveDef.completionRewards.Length; i++)
                    {
                        ResourceManager.Instance.Add(waveDef.completionRewards[i].resource, waveDef.completionRewards[i].amount);
                    }
                }
                GameEvents.RaiseWaveCleared(clearedWave);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(AudioKeys.SfxMiniGameSuccess, 0.85f, 0.03f);
            }

            if (clearedWave >= BalanceDatabase.TotalWaves)
            {
                _phase = DirectorPhase.AllWavesCleared;
                GameManager.Instance?.CheckVictoryCondition();

                // If Wave 5 is cleared and Mutagen < 10, display clear guidance banner!
                if (GameManager.InstanceIfExists != null && GameManager.InstanceIfExists.State != GameState.Victory)
                {
                    GameEvents.RaiseBannerToast(
                        "Волна 5 отбита! Доведите уровень Мутагена до 10 (через Мини-игру или Био-Реактор) для Пробуждения Сердца!",
                        BalanceDatabase.GetResourceColor(ResourceType.Mutagen));
                }
                return;
            }

            // Advance to next wave preparation countdown
            _currentWaveNumber = clearedWave + 1;
            _phase = DirectorPhase.PreparingWave;

            WaveDefinition nextWaveDef = BalanceDatabase.GetWave(_currentWaveNumber);
            _prepCountdownRemaining = nextWaveDef != null && nextWaveDef.prepDurationSec > 5f
                ? nextWaveDef.prepDurationSec
                : 42f;
            _countdownBroadcastAccum = 0f;

            GameManager.InstanceIfExists?.SetCurrentWave(_currentWaveNumber);
            GameEvents.RaiseWaveCountdownTick(_currentWaveNumber, _prepCountdownRemaining);

            GameEvents.RaiseBannerToast(
                $"Волна {clearedWave} отбита! Передышка {Mathf.RoundToInt(_prepCountdownRemaining)}с перед Волной {_currentWaveNumber}!",
                new Color(0.35f, 0.96f, 0.62f, 1f));
        }

        private void HandleResourceChangedForPostWave5Victory(ResourceType type, float newAmount, float delta)
        {
            if (_phase != DirectorPhase.AllWavesCleared || type != ResourceType.Mutagen) return;
            GameManager.InstanceIfExists?.CheckVictoryCondition();
        }
    }
}
