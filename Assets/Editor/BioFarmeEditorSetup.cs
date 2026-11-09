using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BioFarme.Audio;
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
    /// Automated Editor configuration & scene builder for "BioFarme: Сердце Колонии" (Unity 2023.1.22f1).
    /// Configures all 139 pixel-art sprites (128 PPU, Point, Uncompressed, FullRect, Center pivot),
    /// configures 1920x1080 PlayerSettings & WebGL/Standalone readiness, and builds MainMenu.unity & Gameplay.unity.
    /// </summary>
    [InitializeOnLoad]
    public static class BioFarmeEditorSetup
    {
        public const string SetupStampRelativePath = "Temp/biofarme_setup_v1.done";
        private const string SessionRunningGuardKey = "BioFarme_SetupRunning_v1";

        [Serializable]
        private sealed class SetupSummary
        {
            public bool success;
            public string timestampUtc;
            public int totalPngFound;
            public int spritesConfigured;
            public float verifiedSamplePpu;
            public string mainMenuScenePath;
            public string gameplayScenePath;
            public string[] buildScenes;
            public string companyName;
            public string productName;
            public int defaultWidth;
            public int defaultHeight;
        }

        static BioFarmeEditorSetup()
        {
            EditorApplication.delayCall += TryRunAutoSetupOnce;
        }

        private static void TryRunAutoSetupOnce()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRunAutoSetupOnce;
                return;
            }

            string stampPath = Path.Combine(Directory.GetCurrentDirectory(), SetupStampRelativePath);
            if (File.Exists(stampPath))
            {
                return;
            }

            if (SessionState.GetBool(SessionRunningGuardKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionRunningGuardKey, true);
            try
            {
                RunFullProjectSetupInternal();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BioFarmeEditorSetup] Auto-setup failed: {ex}");
            }
            finally
            {
                SessionState.SetBool(SessionRunningGuardKey, false);
            }
        }

        [MenuItem("BioFarme/Run Full Project & Scene Setup")]
        public static void MenuRunFullSetup()
        {
            RunFullProjectSetupInternal();
        }

        [MenuItem("BioFarme/Configure All 139 Sprite Importers (128 PPU Point)")]
        public static void MenuConfigureSpritesOnly()
        {
            var result = ConfigureAllSprites();
            Debug.Log($"[BioFarmeEditorSetup] Configured {result.configured}/{result.found} sprites.");
        }

        [MenuItem("BioFarme/Rebuild MainMenu & Gameplay Scenes")]
        public static void MenuRebuildScenesOnly()
        {
            BuildAndSaveAllScenes();
            Debug.Log("[BioFarmeEditorSetup] Rebuilt MainMenu.unity, Gameplay.unity, and SampleScene.unity.");
        }

        public static void RunFullProjectSetupInternal()
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string tempDir = Path.Combine(projectRoot, "Temp");
            Directory.CreateDirectory(tempDir);
            string stampPath = Path.Combine(projectRoot, SetupStampRelativePath);

            // 1. Configure all 139 sprites under Assets/Resources/Sprites/ + embedded Cyrillic font
            (int foundCount, int configuredCount) = ConfigureAllSprites();
            ConfigureEmbeddedFonts();

            // 2. Configure PlayerSettings (1920x1080, WebGL/Linux ready)
            ConfigurePlayerSettings();

            // 3. Build & Save MainMenu.unity, Gameplay.unity, SampleScene.unity + EditorBuildSettings
            BuildAndSaveAllScenes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SpriteRegistry.RefreshIndex();

            Sprite sampleTile = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Sprites/Tiles/Tile_MossGround_01.png");
            float verifiedPpu = sampleTile != null ? sampleTile.pixelsPerUnit : -1f;

            var summary = new SetupSummary
            {
                success = true,
                timestampUtc = DateTime.UtcNow.ToString("o"),
                totalPngFound = foundCount,
                spritesConfigured = configuredCount,
                verifiedSamplePpu = verifiedPpu,
                mainMenuScenePath = "Assets/Scenes/MainMenu.unity",
                gameplayScenePath = "Assets/Scenes/Gameplay.unity",
                buildScenes = new[]
                {
                    "Assets/Scenes/MainMenu.unity",
                    "Assets/Scenes/Gameplay.unity"
                },
                companyName = PlayerSettings.companyName,
                productName = PlayerSettings.productName,
                defaultWidth = PlayerSettings.defaultScreenWidth,
                defaultHeight = PlayerSettings.defaultScreenHeight
            };

            File.WriteAllText(stampPath, JsonUtility.ToJson(summary, true));
            Debug.Log($"[BioFarmeEditorSetup] Setup complete: {configuredCount}/{foundCount} sprites @ 128 PPU (verified sample PPU={verifiedPpu}), scenes & PlayerSettings saved.");
        }

        public static (int found, int configured) ConfigureAllSprites()
        {
            string spritesAbsDir = Path.Combine(Application.dataPath, "Resources", "Sprites");
            if (!Directory.Exists(spritesAbsDir))
            {
                Debug.LogWarning($"[BioFarmeEditorSetup] Sprites folder not found at {spritesAbsDir}");
                return (0, 0);
            }

            string[] pngFiles = Directory.GetFiles(spritesAbsDir, "*.png", SearchOption.AllDirectories);
            Array.Sort(pngFiles, StringComparer.OrdinalIgnoreCase);

            int configured = 0;
            string dataPathNormalized = Application.dataPath.Replace('\\', '/');

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < pngFiles.Length; i++)
                {
                    string absNormalized = pngFiles[i].Replace('\\', '/');
                    if (!absNormalized.StartsWith(dataPathNormalized, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string assetPath = "Assets" + absNormalized.Substring(dataPathNormalized.Length);
                    TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer == null)
                    {
                        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                        importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    }

                    if (importer == null)
                    {
                        continue;
                    }

                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = 128f;
                    importer.filterMode = FilterMode.Point;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.maxTextureSize = 2048;
                    importer.npotScale = TextureImporterNPOTScale.None;

                    TextureImporterSettings settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    settings.textureType = TextureImporterType.Sprite;
                    settings.spriteMode = (int)SpriteImportMode.Single;
                    settings.spritePixelsPerUnit = 128f;
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    settings.spriteAlignment = (int)SpriteAlignment.Center;
                    settings.spritePivot = new Vector2(0.5f, 0.5f);
                    settings.filterMode = FilterMode.Point;
                    settings.mipmapEnabled = false;
                    settings.alphaIsTransparency = true;
                    settings.npotScale = TextureImporterNPOTScale.None;
                    importer.SetTextureSettings(settings);

                    ConfigurePlatformSettings(importer, "DefaultTexturePlatform");
                    ResetPlatformOverride(importer, "WebGL");
                    ResetPlatformOverride(importer, "Standalone");

                    importer.SaveAndReimport();
                    configured++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return (pngFiles.Length, configured);
        }

        public static void ConfigureEmbeddedFonts()
        {
            const string fontAssetPath = "Assets/Resources/Fonts/LiberationSans.ttf";
            string absFontPath = Path.Combine(Application.dataPath, "Resources", "Fonts", "LiberationSans.ttf");
            if (!File.Exists(absFontPath))
            {
                Debug.LogWarning($"[BioFarmeEditorSetup] Embedded Cyrillic font missing at {absFontPath}");
                return;
            }

            AssetDatabase.ImportAsset(fontAssetPath, ImportAssetOptions.ForceSynchronousImport);
            TrueTypeFontImporter fontImporter = AssetImporter.GetAtPath(fontAssetPath) as TrueTypeFontImporter;
            if (fontImporter != null)
            {
                bool needsSave = false;
                if (!fontImporter.includeFontData)
                {
                    fontImporter.includeFontData = true;
                    needsSave = true;
                }
                if (fontImporter.fontTextureCase != FontTextureCase.Dynamic)
                {
                    fontImporter.fontTextureCase = FontTextureCase.Dynamic;
                    needsSave = true;
                }
                if (needsSave)
                {
                    fontImporter.SaveAndReimport();
                }
            }
        }

        private static void ConfigurePlatformSettings(TextureImporter importer, string platformName)
        {
            TextureImporterPlatformSettings ps = importer.GetPlatformTextureSettings(platformName);
            if (ps == null) return;
            ps.maxTextureSize = 2048;
            ps.textureCompression = TextureImporterCompression.Uncompressed;
            ps.crunchedCompression = false;
            importer.SetPlatformTextureSettings(ps);
        }

        private static void ResetPlatformOverride(TextureImporter importer, string platformName)
        {
            TextureImporterPlatformSettings ps = importer.GetPlatformTextureSettings(platformName);
            if (ps == null) return;
            ps.overridden = false;
            ps.maxTextureSize = 2048;
            ps.textureCompression = TextureImporterCompression.Uncompressed;
            ps.crunchedCompression = false;
            importer.SetPlatformTextureSettings(ps);
        }

        public static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "BioFarme Studio";
            PlayerSettings.productName = "BioFarme: Heart of the Colony";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.defaultWebScreenWidth = 1920;
            PlayerSettings.defaultWebScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = true;
        }

        public static void BuildAndSaveAllScenes()
        {
            string scenesAbsDir = Path.Combine(Application.dataPath, "Scenes");
            Directory.CreateDirectory(scenesAbsDir);

            // 1. Build MainMenu.unity (and mirror to SampleScene.unity)
            Scene menuScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PopulateMainMenuScene();
            EditorSceneManager.SaveScene(menuScene, "Assets/Scenes/SampleScene.unity");
            EditorSceneManager.SaveScene(menuScene, "Assets/Scenes/MainMenu.unity");

            // 2. Build Gameplay.unity
            Scene gameplayScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PopulateGameplayScene();
            EditorSceneManager.SaveScene(gameplayScene, "Assets/Scenes/Gameplay.unity");

            // 3. Wire Build Settings: 0 = MainMenu.unity, 1 = Gameplay.unity
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/MainMenu.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Gameplay.unity", true)
            };

            // Leave MainMenu.unity open in Editor
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        }

        private static void PopulateMainMenuScene()
        {
            // Main Camera
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.05f, 0.06f, 1f);
            cam.nearClipPlane = -20f;
            cam.farClipPlane = 50f;
            camGo.AddComponent<AudioListener>();

            // [GameManager]
            GameObject gmGo = new GameObject("[GameManager]");
            GameManager gm = gmGo.AddComponent<GameManager>();
            SetGameManagerInitialState(gm, GameState.MainMenu);

            // [AudioManager]
            GameObject audioGo = new GameObject("[AudioManager]");
            audioGo.AddComponent<AudioManager>();

            // [MainMenuUI]
            GameObject menuUiGo = new GameObject("[MainMenuUI]");
            menuUiGo.AddComponent<MainMenuUI>();
        }

        private static void PopulateGameplayScene()
        {
            // Main Camera
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(18.5f, 11.5f, -10f);
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6.2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.05f, 0.06f, 1f);
            cam.nearClipPlane = -20f;
            cam.farClipPlane = 50f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<CameraFollowController>();

            // [GameManager]
            GameObject gmGo = new GameObject("[GameManager]");
            GameManager gm = gmGo.AddComponent<GameManager>();
            SetGameManagerInitialState(gm, GameState.Playing);

            // [ResourceManager]
            GameObject rmGo = new GameObject("[ResourceManager]");
            rmGo.AddComponent<ResourceManager>();

            // [AudioManager]
            GameObject audioGo = new GameObject("[AudioManager]");
            audioGo.AddComponent<AudioManager>();

            // [WorldPopupManager]
            GameObject popupGo = new GameObject("[WorldPopupManager]");
            popupGo.AddComponent<WorldPopupManager>();

            // [BioGridManager] (attaches FlowFieldPathfinder & VeinNetworkSystem)
            GameObject gridGo = new GameObject("[BioGridManager]");
            gridGo.AddComponent<FlowFieldPathfinder>();
            gridGo.AddComponent<VeinNetworkSystem>();
            gridGo.AddComponent<BioGridManager>();

            // [StructureManager]
            GameObject structGo = new GameObject("[StructureManager]");
            structGo.AddComponent<StructureManager>();

            // [ProjectileManager]
            GameObject projGo = new GameObject("[ProjectileManager]");
            projGo.AddComponent<ProjectileManager>();

            // [WaveDirector]
            GameObject waveGo = new GameObject("[WaveDirector]");
            waveGo.AddComponent<WaveDirector>();

            // [CropSystem]
            GameObject cropGo = new GameObject("[CropSystem]");
            cropGo.AddComponent<CropSystem>();

            // [BioWeatherSystem]
            GameObject weatherGo = new GameObject("[BioWeatherSystem]");
            weatherGo.AddComponent<BioWeatherSystem>();

            // [ToolHotbarSystem]
            GameObject hotbarGo = new GameObject("[ToolHotbarSystem]");
            hotbarGo.AddComponent<ToolHotbarSystem>();

            // [Player_HazmatEngineer]
            GameObject playerGo = new GameObject("[Player_HazmatEngineer]");
            playerGo.transform.position = new Vector3(18.5f, 8.5f, 0f);
            playerGo.AddComponent<PlayerController>();

            // [GameplayUiRoot]
            GameObject uiRootGo = new GameObject("[GameplayUiRoot]");
            uiRootGo.AddComponent<GameplayUiRoot>();
        }

        private static void SetGameManagerInitialState(GameManager gm, GameState desired)
        {
            if (gm == null) return;
            SerializedObject so = new SerializedObject(gm);
            SerializedProperty prop = so.FindProperty("_initialState");
            if (prop != null)
            {
                prop.enumValueIndex = (int)desired;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
