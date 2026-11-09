using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using BioFarme.Combat;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Factory;
using BioFarme.Farming;
using BioFarme.Grid;
using BioFarme.Player;
using BioFarme.UI;

namespace BioFarme.EditorTools
{
    /// <summary>
    /// Live PlayMode integration QA runner & offscreen 1920x1080 screenshot harness for "BioFarme: Сердце Колонии".
    /// Triggered via Temp/run_playtest.trigger (or BioFarme menu item), survives PlayMode Domain Reloads via SessionState,
    /// exercises the full closed gameplay loop, captures 7 crisp 1920x1080 screenshots (including ScreenSpaceOverlay canvases),
    /// verifies 0 runtime errors/exceptions, leaves MainMenu.unity open, and writes Temp/qa_report.json.
    /// </summary>
    [InitializeOnLoad]
    public static class BioFarmePlaytestRunner
    {
        private const string TriggerRelativePath = "Temp/run_playtest.trigger";
        private const string ReportRelativePath = "Temp/qa_report.json";
        private const string ProgressLogRelativePath = "Temp/playtest_progress.log";
        private const string RuntimeErrorsRelativePath = "Temp/playtest_runtime_errors.log";
        private const string ScreenshotsRelativeDir = "ContestSubmission/Media/Screenshots";

        private const string KeyActive = "BioFarme_QA_Active_v1";
        private const string KeyPhase = "BioFarme_QA_Phase_v1";
        private const string KeyWaitFrames = "BioFarme_QA_WaitFrames_v1";
        private const string KeyStartTicks = "BioFarme_QA_StartTicks_v1";
        private const string KeyStateJson = "BioFarme_QA_StateJson_v1";

        private static int _lastObservedFrame = -1;
        private static int _stalledUpdateTicks;

        private struct CanvasRestoreState
        {
            public Canvas Canvas;
            public RenderMode OrigRenderMode;
            public Camera OrigWorldCamera;
            public float OrigPlaneDistance;
            public int OrigSortingOrder;
        }

        [Serializable]
        public sealed class ScreenshotEntry
        {
            public string fileName;
            public string fullPath;
            public int width;
            public int height;
            public long fileSizeBytes;
        }

        [Serializable]
        public sealed class QaReport
        {
            public bool allChecksPassed;
            public string completedAtUtc;
            public bool mainMenuLoadedCleanly;
            public bool mainMenuNoWorldSystemsSpawned;
            public bool gameplayTransitionSucceeded;
            public bool coreHeartPresentAndAlive;
            public float coreHeartHp;
            public bool bioGridPresent;
            public bool playerPresent;
            public bool tilledSoilSucceeded;
            public bool plantedSporecapSucceeded;
            public bool wateredSoilSucceeded;
            public bool builtBoneWallSucceeded;
            public bool builtVeinBiomassSucceeded;
            public bool builtFlytrapTurretSucceeded;
            public bool waveTriggeredAndEnemiesSpawned;
            public int activeEnemiesObservedInCombat;
            public bool bioCodexOpenedAndClosed;
            public bool tacticalPauseVerified;
            public bool geneSplicingMiniGameVerified;
            public float cropGrowthMultiplierAfterMiniGame;
            public float turretFireRateMultiplierAfterMiniGame;
            public float mutagenAfterMiniGame;
            public bool victoryScreenVerified;
            public bool defeatScreenVerified;
            public string activeSceneAfterExit;
            public int runtimeErrorCount;
            public int consoleErrorCount;
            public List<string> errors = new List<string>();
            public List<ScreenshotEntry> screenshots = new List<ScreenshotEntry>();
        }

        static BioFarmePlaytestRunner()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;

            Application.logMessageReceived -= OnLogMessageReceived;
            Application.logMessageReceived += OnLogMessageReceived;
        }

        [MenuItem("BioFarme/Run Automated PlayMode QA & Capture 7 Screenshots")]
        public static void MenuTriggerPlaytest()
        {
            string triggerPath = Path.Combine(Directory.GetCurrentDirectory(), TriggerRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(triggerPath) ?? "Temp");
            File.WriteAllText(triggerPath, DateTime.UtcNow.ToString("o"));
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (!SessionState.GetBool(KeyActive, false))
            {
                return;
            }

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                try
                {
                    string errPath = Path.Combine(Directory.GetCurrentDirectory(), RuntimeErrorsRelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(errPath) ?? "Temp");
                    string line = $"[{type}] {condition} | {stackTrace?.Replace('\r', ' ').Replace('\n', ' ')}";
                    File.AppendAllLines(errPath, new[] { line });
                }
                catch
                {
                    // Ignore file I/O contention
                }
            }
        }

        private static void LogProgress(string msg)
        {
            try
            {
                string logPath = Path.Combine(Directory.GetCurrentDirectory(), ProgressLogRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? "Temp");
                File.AppendAllLines(logPath, new[] { $"{DateTime.UtcNow:HH:mm:ss.fff} [frame={Time.frameCount} play={EditorApplication.isPlaying}] {msg}" });
            }
            catch
            {
                // Ignore
            }
        }

        private static QaReport LoadState()
        {
            string raw = SessionState.GetString(KeyStateJson, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new QaReport();
            }

            try
            {
                return JsonUtility.FromJson<QaReport>(raw) ?? new QaReport();
            }
            catch
            {
                return new QaReport();
            }
        }

        private static void SaveState(QaReport report)
        {
            SessionState.SetString(KeyStateJson, JsonUtility.ToJson(report));
        }

        private static void OnEditorUpdate()
        {
            bool isActive = SessionState.GetBool(KeyActive, false);
            if (!isActive)
            {
                TryStartFromTriggerFile();
                return;
            }

            int phase = SessionState.GetInt(KeyPhase, 0);

            if (phase == 100)
            {
                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
                    EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    return;
                }

                FinalizeInEditMode();
                return;
            }

            // Safety timeout (90 seconds) so automated QA can never hang forever
            long startTicks = long.TryParse(SessionState.GetString(KeyStartTicks, "0"), out long parsed) ? parsed : 0L;
            if (startTicks > 0L && (DateTime.UtcNow.Ticks - startTicks) > TimeSpan.FromSeconds(90).Ticks)
            {
                LogProgress("TIMEOUT reached (90s) - aborting PlayMode and writing diagnostic report.");
                QaReport timedOut = LoadState();
                timedOut.errors.Add("Playtest timed out after 90 seconds at phase " + phase);
                SaveState(timedOut);
                Time.captureFramerate = 0;
                SessionState.SetString(KeyStartTicks, "0");
                SessionState.SetInt(KeyPhase, 100);
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                    return;
                }

                FinalizeInEditMode();
                return;
            }

            // PlayMode execution phases (1..9)
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            if (EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
            }

            Application.runInBackground = true;
            Time.captureFramerate = 60;
            EditorApplication.QueuePlayerLoopUpdate();
            InternalEditorUtility.RepaintAllViews();

            if (Time.frameCount == _lastObservedFrame)
            {
                _stalledUpdateTicks++;
                if (_stalledUpdateTicks == 25)
                {
                    FocusGameViewTabIfPresent();
                }
                return;
            }

            _lastObservedFrame = Time.frameCount;
            _stalledUpdateTicks = 0;

            int waitFrames = SessionState.GetInt(KeyWaitFrames, 0);
            if (waitFrames > 0)
            {
                SessionState.SetInt(KeyWaitFrames, waitFrames - 1);
                return;
            }

            try
            {
                ExecutePlayModePhase(phase);
            }
            catch (Exception ex)
            {
                LogProgress($"EXCEPTION in phase {phase}: {ex}");
                QaReport rep = LoadState();
                rep.errors.Add($"Phase {phase} exception: {ex.Message} | {ex.StackTrace}");
                SaveState(rep);

                Time.captureFramerate = 0;
                SessionState.SetInt(KeyPhase, 100);
                EditorApplication.isPlaying = false;
            }
        }

        private static void TryStartFromTriggerFile()
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string triggerPath = Path.Combine(projectRoot, TriggerRelativePath);
            if (!File.Exists(triggerPath))
            {
                return;
            }

            string setupDonePath = Path.Combine(projectRoot, BioFarmeEditorSetup.SetupStampRelativePath);
            if (!File.Exists(setupDonePath))
            {
                // Wait for BioFarmeEditorSetup one-shot initialization to finish first
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            File.Delete(triggerPath);

            string reportPath = Path.Combine(projectRoot, ReportRelativePath);
            if (File.Exists(reportPath)) File.Delete(reportPath);

            string errPath = Path.Combine(projectRoot, RuntimeErrorsRelativePath);
            if (File.Exists(errPath)) File.Delete(errPath);

            string progPath = Path.Combine(projectRoot, ProgressLogRelativePath);
            if (File.Exists(progPath)) File.Delete(progPath);

            ClearUnityConsole();

            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
            FocusGameViewTabIfPresent();

            QaReport fresh = new QaReport();
            SaveState(fresh);

            SessionState.SetString(KeyStartTicks, DateTime.UtcNow.Ticks.ToString());
            SessionState.SetInt(KeyPhase, 1);
            SessionState.SetInt(KeyWaitFrames, 18);
            SessionState.SetBool(KeyActive, true);
            _lastObservedFrame = -1;
            _stalledUpdateTicks = 0;

            LogProgress("Starting PlayMode QA from MainMenu.unity...");
            EditorApplication.isPlaying = true;
        }

        private static void ExecutePlayModePhase(int phase)
        {
            QaReport rep = LoadState();

            switch (phase)
            {
                case 1:
                {
                    // Verify MainMenu.unity state & hygiene, capture 01_MainMenu.png, then transition to Gameplay
                    string sceneName = SceneManager.GetActiveScene().name;
                    rep.mainMenuLoadedCleanly =
                        string.Equals(sceneName, "MainMenu", StringComparison.OrdinalIgnoreCase) &&
                        GameManager.Instance != null &&
                        GameManager.Instance.State == GameState.MainMenu &&
                        UnityEngine.Object.FindFirstObjectByType<MainMenuUI>() != null;

                    rep.mainMenuNoWorldSystemsSpawned =
                        BioGridManager.InstanceIfExists == null &&
                        PlayerController.InstanceIfExists == null &&
                        StructureManager.InstanceIfExists == null &&
                        CropSystem.InstanceIfExists == null &&
                        WaveDirector.InstanceIfExists == null;

                    ScreenshotEntry s1 = CaptureScreenshot1920x1080("01_MainMenu.png");
                    rep.screenshots.Add(s1);
                    LogProgress($"Captured 01_MainMenu.png (clean={rep.mainMenuLoadedCleanly}, noWorld={rep.mainMenuNoWorldSystemsSpawned}). Calling StartNewGame()...");

                    SaveState(rep);
                    GameManager.Instance.StartNewGame();

                    SessionState.SetInt(KeyPhase, 2);
                    SessionState.SetInt(KeyWaitFrames, 26);
                    break;
                }

                case 2:
                {
                    // Ensure Gameplay scene has finished loading
                    if (!string.Equals(SceneManager.GetActiveScene().name, "Gameplay", StringComparison.OrdinalIgnoreCase))
                    {
                        SessionState.SetInt(KeyWaitFrames, 5);
                        return;
                    }

                    rep.gameplayTransitionSucceeded =
                        GameManager.Instance != null &&
                        GameManager.Instance.State == GameState.Playing;

                    rep.coreHeartPresentAndAlive =
                        CoreHeartEntity.Instance != null &&
                        CoreHeartEntity.Instance.CurrentHp > 0f;

                    rep.coreHeartHp = CoreHeartEntity.Instance != null ? CoreHeartEntity.Instance.CurrentHp : 0f;
                    rep.bioGridPresent = BioGridManager.Instance != null;
                    rep.playerPresent = PlayerController.Instance != null;

                    BioGridManager grid = BioGridManager.Instance;
                    CropSystem crops = CropSystem.Instance;
                    StructureManager structs = StructureManager.Instance;
                    WaveDirector waves = WaveDirector.Instance;

                    // 1) Till soil, plant Sporecap, and irrigate
                    Vector2Int tillCell = FindValidTillableCell(grid, new Vector2Int(16, 8));
                    rep.tilledSoilSucceeded = grid != null && grid.TillTile(tillCell);
                    rep.plantedSporecapSucceeded = crops != null && crops.TryPlantCrop(tillCell, CropType.Sporecap);
                    rep.wateredSoilSucceeded = grid != null && grid.WaterTile(tillCell, 1.0f) && grid.IsTileWatered(tillCell);

                    // Also water & plant on the eastern garden bed so colony crops look lush
                    if (grid != null && crops != null)
                    {
                        grid.WaterTile(new Vector2Int(20, 9), 1.0f);
                        grid.WaterTile(new Vector2Int(21, 9), 1.0f);
                        grid.WaterTile(new Vector2Int(22, 9), 1.0f);
                        crops.TryPlantCrop(new Vector2Int(20, 9), CropType.CrimsonPod);
                    }

                    // 2) Build BoneWall, VeinBiomass, and FlytrapTurret
                    // Ensure sufficient resources so all 3 builds deterministically succeed
                    if (ResourceManager.Instance != null)
                    {
                        ResourceManager.Instance.Add(ResourceType.Biomass, 80f);
                        ResourceManager.Instance.Add(ResourceType.BoneChitin, 15f);
                        ResourceManager.Instance.Add(ResourceType.Spores, 25f);
                    }

                    Vector2Int wallCell = FindValidPlacementCell(grid, StructureType.BoneWall,
                        new Vector2Int(10, 9), new Vector2Int(10, 15), new Vector2Int(25, 9));
                    rep.builtBoneWallSucceeded = structs != null && structs.TryBuildStructure(wallCell, StructureType.BoneWall);

                    Vector2Int veinCell = FindValidPlacementCell(grid, StructureType.VeinBiomass,
                        new Vector2Int(11, 12), new Vector2Int(24, 12), new Vector2Int(16, 10));
                    rep.builtVeinBiomassSucceeded = structs != null && structs.TryBuildStructure(veinCell, StructureType.VeinBiomass);

                    Vector2Int turretCell = FindValidPlacementCell(grid, StructureType.FlytrapTurret,
                        new Vector2Int(11, 13), new Vector2Int(24, 13), new Vector2Int(13, 11));
                    rep.builtFlytrapTurretSucceeded = structs != null && structs.TryBuildStructure(turretCell, StructureType.FlytrapTurret);

                    // 3) Trigger early wave & spawn mutant bugs into active turret engagement range
                    GameEvents.RaiseSendNextWaveEarlyRequested();
                    if (waves != null)
                    {
                        waves.SpawnEnemy(EnemyType.SwarmMite, new Vector2(9.1f, 12.3f));
                        waves.SpawnEnemy(EnemyType.MantisBug, new Vector2(9.7f, 11.7f));
                        waves.SpawnEnemy(EnemyType.SwarmMite, new Vector2(8.6f, 12.7f));
                        waves.SpawnEnemy(EnemyType.SwarmMite, new Vector2(26.2f, 12.3f));
                        waves.SpawnEnemy(EnemyType.CarapaceCrab, new Vector2(25.7f, 11.8f));
                    }

                    rep.waveTriggeredAndEnemiesSpawned =
                        waves != null && waves.IsWaveInProgress && WorldEntityRegistry.ActiveEnemies.Count > 0;

                    // Position Engineer & Camera to frame the Living Heart Core, crops, veins, turrets & bug assault
                    if (PlayerController.Instance != null)
                    {
                        PlayerController.Instance.transform.position = new Vector3(18.5f, 9.8f, 0f);
                    }
                    if (ToolHotbarSystem.Instance != null)
                    {
                        ToolHotbarSystem.Instance.SelectSlot(1); // BioSprayer
                    }
                    if (CameraFollowController.Instance != null)
                    {
                        CameraFollowController.Instance.SnapToPosition(new Vector2(18.5f, 11.0f));
                    }

                    SaveState(rep);
                    LogProgress("Colony populated & wave triggered; advancing 40 combat frames for projectiles & vein packets...");

                    SessionState.SetInt(KeyPhase, 3);
                    SessionState.SetInt(KeyWaitFrames, 40);
                    break;
                }

                case 3:
                {
                    // Capture 02_Gameplay_ColonyAndCombat.png, then open [B] BioCodexWindowUI
                    WorldEntityRegistry.PruneDeadEnemies();
                    rep.activeEnemiesObservedInCombat = WorldEntityRegistry.ActiveEnemies.Count;

                    if (CameraFollowController.Instance != null)
                    {
                        CameraFollowController.Instance.SnapToPosition(new Vector2(18.5f, 11.1f));
                    }

                    ScreenshotEntry s2 = CaptureScreenshot1920x1080("02_Gameplay_ColonyAndCombat.png");
                    rep.screenshots.Add(s2);
                    LogProgress($"Captured 02_Gameplay_ColonyAndCombat.png (activeEnemies={rep.activeEnemiesObservedInCombat}). Opening BioCodex...");

                    if (GameplayUiRoot.Instance != null)
                    {
                        GameplayUiRoot.Instance.ToggleBioCodex();
                    }

                    SaveState(rep);
                    SessionState.SetInt(KeyPhase, 4);
                    SessionState.SetInt(KeyWaitFrames, 6);
                    break;
                }

                case 4:
                {
                    // Capture 03_BioCodex_ShopAndPriorities.png, close it, then trigger TacticalPause
                    bool openedOk = GameplayUiRoot.Instance != null &&
                                    GameplayUiRoot.Instance.BioCodex != null &&
                                    GameplayUiRoot.Instance.BioCodex.IsOpen;

                    ScreenshotEntry s3 = CaptureScreenshot1920x1080("03_BioCodex_ShopAndPriorities.png");
                    rep.screenshots.Add(s3);

                    if (GameplayUiRoot.Instance != null && GameplayUiRoot.Instance.BioCodex != null)
                    {
                        GameplayUiRoot.Instance.BioCodex.Close();
                    }

                    bool closedOk = GameplayUiRoot.Instance != null &&
                                    GameplayUiRoot.Instance.BioCodex != null &&
                                    !GameplayUiRoot.Instance.BioCodex.IsOpen;

                    rep.bioCodexOpenedAndClosed = openedOk && closedOk;
                    LogProgress($"Captured 03_BioCodex_ShopAndPriorities.png (openedAndClosed={rep.bioCodexOpenedAndClosed}). Entering TacticalPause...");

                    if (ToolHotbarSystem.Instance != null)
                    {
                        ToolHotbarSystem.Instance.SelectSlot(5);
                    }
                    GameManager.Instance.ToggleTacticalPause();

                    SaveState(rep);
                    SessionState.SetInt(KeyPhase, 5);
                    SessionState.SetInt(KeyWaitFrames, 6);
                    break;
                }

                case 5:
                {
                    // Capture 04_TacticalPause_Planning.png, resume, then open GeneSplicing MiniGame
                    bool wasPaused = GameManager.Instance != null && GameManager.Instance.IsTacticalPaused;

                    ScreenshotEntry s4 = CaptureScreenshot1920x1080("04_TacticalPause_Planning.png");
                    rep.screenshots.Add(s4);

                    GameManager.Instance.ToggleTacticalPause();
                    bool resumedOk = GameManager.Instance != null && GameManager.Instance.State == GameState.Playing;
                    rep.tacticalPauseVerified = wasPaused && resumedOk;
                    LogProgress($"Captured 04_TacticalPause_Planning.png (verified={rep.tacticalPauseVerified}). Opening GeneSplicing MiniGame...");

                    GameManager.Instance.OpenMiniGame(MiniGameKind.GeneSplicing);

                    SaveState(rep);
                    SessionState.SetInt(KeyPhase, 6);
                    SessionState.SetInt(KeyWaitFrames, 14);
                    break;
                }

                case 6:
                {
                    // Capture 05_MiniGame_GeneSplicing.png, complete via CompleteMiniGame(GeneSplicing, true, 3), verify buffs & Mutagen, then TriggerVictory
                    bool miniOpen = GameManager.Instance != null &&
                                    GameManager.Instance.State == GameState.MiniGame &&
                                    GameplayUiRoot.Instance != null &&
                                    GameplayUiRoot.Instance.MiniGames != null &&
                                    GameplayUiRoot.Instance.MiniGames.IsOpen;

                    ScreenshotEntry s5 = CaptureScreenshot1920x1080("05_MiniGame_GeneSplicing.png");
                    rep.screenshots.Add(s5);

                    GameManager.Instance.CompleteMiniGame(MiniGameKind.GeneSplicing, true, 3);

                    float cropMult = ResourceManager.Instance != null ? ResourceManager.Instance.CropGrowthMultiplier : 1f;
                    float turretMult = ResourceManager.Instance != null ? ResourceManager.Instance.TurretFireRateMultiplier : 1f;
                    float mutagen = ResourceManager.Instance != null ? ResourceManager.Instance.Get(ResourceType.Mutagen) : 0f;

                    rep.cropGrowthMultiplierAfterMiniGame = cropMult;
                    rep.turretFireRateMultiplierAfterMiniGame = turretMult;
                    rep.mutagenAfterMiniGame = mutagen;
                    rep.geneSplicingMiniGameVerified = miniOpen && cropMult > 1f && mutagen > 0f;

                    LogProgress($"Captured 05_MiniGame_GeneSplicing.png (cropMult={cropMult:0.00}, mutagen={mutagen:0.0}). Triggering Victory...");

                    GameManager.Instance.TriggerVictory();

                    SaveState(rep);
                    SessionState.SetInt(KeyPhase, 7);
                    SessionState.SetInt(KeyWaitFrames, 6);
                    break;
                }

                case 7:
                {
                    // Capture 06_VictoryScreen.png, then SetState(Playing) & TriggerDefeat
                    rep.victoryScreenVerified = GameManager.Instance != null && GameManager.Instance.State == GameState.Victory;

                    ScreenshotEntry s6 = CaptureScreenshot1920x1080("06_VictoryScreen.png");
                    rep.screenshots.Add(s6);
                    LogProgress($"Captured 06_VictoryScreen.png (verified={rep.victoryScreenVerified}). Triggering Defeat...");

                    GameManager.Instance.SetState(GameState.Playing);
                    GameManager.Instance.TriggerDefeat("Живое Сердце колонии было разрушено натиском Роя!");

                    SaveState(rep);
                    SessionState.SetInt(KeyPhase, 8);
                    SessionState.SetInt(KeyWaitFrames, 6);
                    break;
                }

                case 8:
                {
                    // Capture 07_DefeatScreen.png, then exit PlayMode
                    rep.defeatScreenVerified =
                        GameManager.Instance != null &&
                        GameManager.Instance.State == GameState.Defeat;

                    ScreenshotEntry s7 = CaptureScreenshot1920x1080("07_DefeatScreen.png");
                    rep.screenshots.Add(s7);
                    LogProgress($"Captured 07_DefeatScreen.png (verified={rep.defeatScreenVerified}). Exiting PlayMode...");

                    SaveState(rep);
                    Time.captureFramerate = 0;
                    SessionState.SetInt(KeyPhase, 100);
                    EditorApplication.isPlaying = false;
                    break;
                }
            }
        }

        private static void FinalizeInEditMode()
        {
            SessionState.SetBool(KeyActive, false);
            SessionState.SetInt(KeyPhase, 0);
            SessionState.SetString(KeyStartTicks, "0");

            string projectRoot = Directory.GetCurrentDirectory();
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
            AssetDatabase.SaveAssets();

            QaReport rep = LoadState();
            rep.completedAtUtc = DateTime.UtcNow.ToString("o");
            rep.activeSceneAfterExit = SceneManager.GetActiveScene().name;

            // Collect any runtime errors captured by Application.logMessageReceived
            string errFile = Path.Combine(projectRoot, RuntimeErrorsRelativePath);
            if (File.Exists(errFile))
            {
                string[] lines = File.ReadAllLines(errFile);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                    {
                        rep.errors.Add(lines[i]);
                    }
                }
            }
            rep.runtimeErrorCount = rep.errors.Count;

            // Also inspect Unity Editor Console LogEntries via reflection
            int consoleErrs = ReadConsoleErrorCount(rep.errors);
            rep.consoleErrorCount = consoleErrs;

            bool allScreenshotsValid = rep.screenshots.Count == 7;
            for (int i = 0; i < rep.screenshots.Count; i++)
            {
                ScreenshotEntry sc = rep.screenshots[i];
                if (sc.width != 1920 || sc.height != 1080 || sc.fileSizeBytes < 5000 || !File.Exists(sc.fullPath))
                {
                    allScreenshotsValid = false;
                }
            }

            rep.allChecksPassed =
                rep.mainMenuLoadedCleanly &&
                rep.mainMenuNoWorldSystemsSpawned &&
                rep.gameplayTransitionSucceeded &&
                rep.coreHeartPresentAndAlive &&
                rep.bioGridPresent &&
                rep.playerPresent &&
                rep.tilledSoilSucceeded &&
                rep.plantedSporecapSucceeded &&
                rep.wateredSoilSucceeded &&
                rep.builtBoneWallSucceeded &&
                rep.builtVeinBiomassSucceeded &&
                rep.builtFlytrapTurretSucceeded &&
                rep.waveTriggeredAndEnemiesSpawned &&
                rep.bioCodexOpenedAndClosed &&
                rep.tacticalPauseVerified &&
                rep.geneSplicingMiniGameVerified &&
                rep.victoryScreenVerified &&
                rep.defeatScreenVerified &&
                allScreenshotsValid &&
                rep.runtimeErrorCount == 0 &&
                rep.consoleErrorCount == 0;

            string reportPath = Path.Combine(projectRoot, ReportRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? "Temp");
            File.WriteAllText(reportPath, JsonUtility.ToJson(rep, true));

            LogProgress($"QA COMPLETE! allChecksPassed={rep.allChecksPassed}, screenshots={rep.screenshots.Count}, runtimeErrors={rep.runtimeErrorCount}, consoleErrors={rep.consoleErrorCount}");
            Debug.Log($"[BioFarmePlaytestRunner] QA report written to {reportPath} (allChecksPassed={rep.allChecksPassed}).");
        }

        private static Vector2Int FindValidTillableCell(BioGridManager grid, Vector2Int preferred)
        {
            if (grid == null) return preferred;
            if (grid.CanTill(preferred)) return preferred;

            for (int r = 1; r <= 5; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        Vector2Int c = new Vector2Int(preferred.x + dx, preferred.y + dy);
                        if (grid.CanTill(c)) return c;
                    }
                }
            }
            return preferred;
        }

        private static Vector2Int FindValidPlacementCell(BioGridManager grid, StructureType type, params Vector2Int[] candidates)
        {
            if (grid == null) return candidates.Length > 0 ? candidates[0] : new Vector2Int(16, 10);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (grid.CanPlaceStructure(candidates[i], type))
                {
                    return candidates[i];
                }
            }

            Vector2Int fallbackOrigin = new Vector2Int(16, 10);
            for (int r = 1; r <= 6; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        Vector2Int c = new Vector2Int(fallbackOrigin.x + dx, fallbackOrigin.y + dy);
                        if (grid.CanPlaceStructure(c, type)) return c;
                    }
                }
            }
            return candidates[0];
        }

        private static ScreenshotEntry CaptureScreenshot1920x1080(string fileName)
        {
            string absDir = Path.Combine(Directory.GetCurrentDirectory(), ScreenshotsRelativeDir);
            Directory.CreateDirectory(absDir);
            string fullPath = Path.Combine(absDir, fileName);

            Camera cam = Camera.main;
            if (cam == null)
            {
                cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
            }
            if (cam == null)
            {
                throw new InvalidOperationException($"No Camera found in active scene '{SceneManager.GetActiveScene().name}' for screenshot {fileName}");
            }

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture prevCamTarget = cam.targetTexture;

            RenderTexture rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                filterMode = FilterMode.Point
            };
            rt.Create();

            var switched = new List<CanvasRestoreState>(4);
            try
            {
                // Set camera targetTexture FIRST so ScreenSpaceCamera CanvasScaler sees 1920x1080 pixel dimensions!
                cam.targetTexture = rt;

                Canvas[] allCanvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < allCanvases.Length; i++)
                {
                    Canvas c = allCanvases[i];
                    if (c == null || !c.isActiveAndEnabled || !c.isRootCanvas) continue;
                    if (c.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        switched.Add(new CanvasRestoreState
                        {
                            Canvas = c,
                            OrigRenderMode = c.renderMode,
                            OrigWorldCamera = c.worldCamera,
                            OrigPlaneDistance = c.planeDistance,
                            OrigSortingOrder = c.sortingOrder
                        });

                        c.renderMode = RenderMode.ScreenSpaceCamera;
                        c.worldCamera = cam;
                        c.planeDistance = 1f;
                        c.sortingOrder = c.sortingOrder + 1000;
                    }
                }

                Canvas.ForceUpdateCanvases();
                for (int i = 0; i < switched.Count; i++)
                {
                    RectTransform crt = switched[i].Canvas.GetComponent<RectTransform>();
                    if (crt != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
                    }
                }
                Canvas.ForceUpdateCanvases();

                cam.Render();

                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0f, 0f, 1920f, 1080f), 0, 0, false);
                tex.Apply(false, false);

                byte[] bytes = tex.EncodeToPNG();
                File.WriteAllBytes(fullPath, bytes);
                UnityEngine.Object.DestroyImmediate(tex);

                return new ScreenshotEntry
                {
                    fileName = fileName,
                    fullPath = fullPath,
                    width = 1920,
                    height = 1080,
                    fileSizeBytes = bytes.LongLength
                };
            }
            finally
            {
                for (int i = 0; i < switched.Count; i++)
                {
                    CanvasRestoreState st = switched[i];
                    if (st.Canvas != null)
                    {
                        st.Canvas.renderMode = st.OrigRenderMode;
                        st.Canvas.worldCamera = st.OrigWorldCamera;
                        st.Canvas.planeDistance = st.OrigPlaneDistance;
                        st.Canvas.sortingOrder = st.OrigSortingOrder;
                    }
                }

                cam.targetTexture = prevCamTarget;
                RenderTexture.active = prevActive;
                Canvas.ForceUpdateCanvases();

                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static void ClearUnityConsole()
        {
            try
            {
                Type logEntriesType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                MethodInfo clearMethod = logEntriesType?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                clearMethod?.Invoke(null, null);
            }
            catch
            {
                // Ignore if unavailable
            }
        }

        private static int ReadConsoleErrorCount(List<string> errorSink)
        {
            try
            {
                Type logEntriesType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                if (logEntriesType == null) return 0;

                MethodInfo countsMethod = logEntriesType.GetMethod("GetCountsByType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (countsMethod != null)
                {
                    object[] args = { 0, 0, 0 };
                    countsMethod.Invoke(null, args);
                    int errorCount = (int)args[0];
                    return errorCount;
                }
            }
            catch (Exception ex)
            {
                errorSink?.Add("LogEntries reflection warning: " + ex.Message);
            }
            return 0;
        }

        private static void FocusGameViewTabIfPresent()
        {
            try
            {
                Type gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gameViewType != null)
                {
                    EditorWindow.FocusWindowIfItsOpen(gameViewType);
                }
            }
            catch
            {
                // Ignore
            }
        }
    }
}
