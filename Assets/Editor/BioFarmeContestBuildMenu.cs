using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BioFarme.EditorTools
{
    /// <summary>
    /// One-click Contest Build exporters for WebGL and Astra Linux (x86_64) directly into ContestSubmission/Build/.
    /// Ensures embedded Cyrillic font (Assets/Resources/Fonts/LiberationSans.ttf) has includeFontData=true
    /// and upgrades WebGL index.html for crisp 16:9 viewport scaling without browser scrollbars.
    /// </summary>
    [InitializeOnLoad]
    public static class BioFarmeContestBuildMenu
    {
        private const string AutoWebGlCyrillicStamp = "Temp/webgl_cyrillic_fix_v1.done";
        private const string AutoRebuildSessionGuard = "BioFarme_WebGLCyrillicRebuildRunning_v1";

        private static readonly string[] BuildScenes =
        {
            "Assets/Scenes/MainMenu.unity",
            "Assets/Scenes/Gameplay.unity"
        };

        static BioFarmeContestBuildMenu()
        {
            EditorApplication.delayCall += TryAutoRebuildWebGLWithCyrillicOnce;
        }

        private static void TryAutoRebuildWebGLWithCyrillicOnce()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoRebuildWebGLWithCyrillicOnce;
                return;
            }

            // Always ensure the embedded Cyrillic font importer is ready
            BioFarmeEditorSetup.ConfigureEmbeddedFonts();

            string projectRoot = Directory.GetCurrentDirectory();
            string stampPath = Path.Combine(projectRoot, AutoWebGlCyrillicStamp);
            if (File.Exists(stampPath)) return;

            string existingWebGlDir = Path.Combine(projectRoot, "ContestSubmission", "Build", "BioFarme_WebGL");
            if (!Directory.Exists(existingWebGlDir)) return;
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL)) return;
            if (SessionState.GetBool(AutoRebuildSessionGuard, false)) return;

            SessionState.SetBool(AutoRebuildSessionGuard, true);
            try
            {
                Debug.Log("[BioFarmeBuild] Auto-rebuilding WebGL release with embedded Cyrillic font (LiberationSans.ttf)...");
                bool ok = BuildWebGLInternal(revealInFinder: false);
                File.WriteAllText(stampPath, $"{{\"rebuilt\":{(ok ? "true" : "false")},\"utc\":\"{DateTime.UtcNow:o}\"}}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BioFarmeBuild] Auto WebGL rebuild error: {ex}");
            }
            finally
            {
                SessionState.SetBool(AutoRebuildSessionGuard, false);
            }
        }

        [MenuItem("BioFarme/Build Contest WebGL Release (ContestSubmission/Build/WebGL)", false, 10)]
        public static void BuildWebGLContestRelease()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                EditorUtility.DisplayDialog(
                    "WebGL Module Not Installed",
                    "В Unity Hub для версии 2023.1.22f1 ещё не установлен модуль 'WebGL Build Support'.\n\n" +
                    "Откройте Unity Hub -> Installs -> 2023.1.22f1 -> Add modules -> отметьте 'WebGL Build Support', " +
                    "перезапустите редактор и снова нажмите этот пункт меню.",
                    "Понятно");
                return;
            }

            BuildWebGLInternal(revealInFinder: true);
        }

        private static bool BuildWebGLInternal(bool revealInFinder)
        {
            BioFarmeEditorSetup.ConfigureEmbeddedFonts();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            PlayerSettings.defaultWebScreenWidth = 1920;
            PlayerSettings.defaultWebScreenHeight = 1080;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = true;

            string outDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "ContestSubmission", "Build", "BioFarme_WebGL"));
            Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes = BuildScenes,
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                WriteLocalWebServerHelpers(outDir);
                PatchWebGlIndexForResponsive16x9(outDir);
                Debug.Log($"[BioFarmeBuild] WebGL build succeeded (Cyrillic font embedded): {outDir}");
                if (revealInFinder)
                {
                    EditorUtility.RevealInFinder(Path.Combine(outDir, "index.html"));
                }
                return true;
            }

            Debug.LogError($"[BioFarmeBuild] WebGL build failed: {report.summary.result}");
            return false;
        }

        [MenuItem("BioFarme/Build Contest Astra Linux Release (ContestSubmission/Build/AstraLinux)", false, 11)]
        public static void BuildLinuxContestRelease()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
            {
                EditorUtility.DisplayDialog(
                    "Linux Build Support Not Installed",
                    "В Unity Hub для версии 2023.1.22f1 не установлен модуль 'Linux Build Support (Mono / IL2CPP)'.\n\n" +
                    "Откройте Unity Hub -> Installs -> 2023.1.22f1 -> Add modules -> отметьте 'Linux Build Support', " +
                    "перезапустите редактор и снова нажмите этот пункт меню.",
                    "Понятно");
                return;
            }

            BioFarmeEditorSetup.ConfigureEmbeddedFonts();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;

            string outDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "ContestSubmission", "Build", "BioFarme_AstraLinux"));
            Directory.CreateDirectory(outDir);
            string exePath = Path.Combine(outDir, "BioFarme.x86_64");

            var options = new BuildPlayerOptions
            {
                scenes = BuildScenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                File.WriteAllText(Path.Combine(outDir, "run_biofarme.sh"),
                    "#!/usr/bin/env bash\ncd \"$(dirname \"$0\")\"\nchmod +x ./BioFarme.x86_64\n./BioFarme.x86_64 -screen-width 1920 -screen-height 1080\n");
                Debug.Log($"[BioFarmeBuild] Astra Linux x86_64 build succeeded: {exePath}");
                EditorUtility.RevealInFinder(exePath);
            }
            else
            {
                Debug.LogError($"[BioFarmeBuild] Linux build failed: {report.summary.result}");
            }
        }

        private static void PatchWebGlIndexForResponsive16x9(string webGlDir)
        {
            string indexPath = Path.Combine(webGlDir, "index.html");
            if (File.Exists(indexPath))
            {
                string html = File.ReadAllText(indexPath);
                string buildTag = DateTime.UtcNow.Ticks.ToString();

                // Cache-bust WebGL data/wasm/js bundles so browser reload always picks up fresh builds
                html = html.Replace("/BioFarme_WebGL.loader.js\"", $"/BioFarme_WebGL.loader.js?v={buildTag}\"");
                html = html.Replace("/BioFarme_WebGL.data\"", $"/BioFarme_WebGL.data?v={buildTag}\"");
                html = html.Replace("/BioFarme_WebGL.framework.js\"", $"/BioFarme_WebGL.framework.js?v={buildTag}\"");
                html = html.Replace("/BioFarme_WebGL.wasm\"", $"/BioFarme_WebGL.wasm?v={buildTag}\"");

                const string fixedSizeSnippet = "canvas.style.width = \"1920px\";\n        canvas.style.height = \"1080px\";";
                const string fixedSizeSnippetCrLf = "canvas.style.width = \"1920px\";\r\n        canvas.style.height = \"1080px\";";
                const string responsiveSnippet =
                    "function fitBioFarmeCanvas() {\n" +
                    "          var vw = window.innerWidth, vh = window.innerHeight;\n" +
                    "          var w = vw, h = Math.round(vw * 9 / 16);\n" +
                    "          if (h > vh) { h = vh; w = Math.round(vh * 16 / 9); }\n" +
                    "          canvas.style.width = w + \"px\";\n" +
                    "          canvas.style.height = h + \"px\";\n" +
                    "        }\n" +
                    "        fitBioFarmeCanvas();\n" +
                    "        window.addEventListener(\"resize\", fitBioFarmeCanvas);";

                if (html.Contains(fixedSizeSnippetCrLf))
                {
                    html = html.Replace(fixedSizeSnippetCrLf, responsiveSnippet);
                }
                else if (html.Contains(fixedSizeSnippet))
                {
                    html = html.Replace(fixedSizeSnippet, responsiveSnippet);
                }
                File.WriteAllText(indexPath, html);
            }

            string cssPath = Path.Combine(webGlDir, "TemplateData", "style.css");
            if (File.Exists(cssPath))
            {
                string css = File.ReadAllText(cssPath);
                if (!css.Contains("overflow: hidden"))
                {
                    css += "\nhtml, body { width: 100%; height: 100%; background: #0d0a0f; overflow: hidden; }\n" +
                           "#unity-footer { display: none; }\n";
                    File.WriteAllText(cssPath, css);
                }
            }
        }

        private static void WriteLocalWebServerHelpers(string webGlDir)
        {
            File.WriteAllText(Path.Combine(webGlDir, "run_local_web.py"),
                "import http.server, socketserver, webbrowser, os\n" +
                "os.chdir(os.path.dirname(os.path.abspath(__file__)))\n" +
                "PORT = 8080\n" +
                "class NoCacheHandler(http.server.SimpleHTTPRequestHandler):\n" +
                "    def end_headers(self):\n" +
                "        self.send_header('Cache-Control', 'no-store, no-cache, must-revalidate, max-age=0')\n" +
                "        super().end_headers()\n" +
                "socketserver.TCPServer.allow_reuse_address = True\n" +
                "print(f'Starting BioFarme local WebGL server at http://localhost:{PORT}')\n" +
                "webbrowser.open(f'http://localhost:{PORT}/index.html')\n" +
                "with socketserver.TCPServer(('', PORT), NoCacheHandler) as httpd:\n" +
                "    httpd.serve_forever()\n");

            File.WriteAllText(Path.Combine(webGlDir, "run_local_linux_mac.sh"),
                "#!/usr/bin/env bash\ncd \"$(dirname \"$0\")\"\npython3 run_local_web.py\n");
        }
    }
}
