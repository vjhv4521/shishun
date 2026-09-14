using System;
using System.IO;
using System.Linq;
using HybridCLR.Editor;
using Haven.Framework.HotUpdate;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Haven.Framework.Editor
{
    public static class HavenPlayerBuilder
    {
        [MenuItem("Haven/Build/Windows Dedicated Server")]
        public static void BuildWindowsDedicatedServer()
        {
            PrepareScene();
            BuildWindows(
                "Build/WindowsServer/HavenServer.exe",
                StandaloneBuildSubtarget.Server,
                ScriptingImplementation.Mono2x,
                BuildOptions.Development,
                useHostedContent: false);
        }

        [MenuItem("Haven/Build/Windows Client (HybridCLR)")]
        public static void BuildWindowsClient()
        {
            PrepareScene();
            HavenFrameworkSetup.GenerateAllAndPrepareAssets();
            HavenContentBuilder.BuildBaselineForPlayer();
            BuildWindows(
                "Build/WindowsClient/HavenClient.exe",
                StandaloneBuildSubtarget.Player,
                ScriptingImplementation.IL2CPP,
                BuildOptions.Development | BuildOptions.AllowDebugging,
                useHostedContent: true);
        }

        // Entry points for -executeMethod batch mode.
        public static void BuildWindowsDedicatedServerBatch() => BuildWindowsDedicatedServer();
        public static void BuildWindowsClientBatch() => BuildWindowsClient();

        private static void PrepareScene()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(HavenNetworkSetup.ScenePath))
                HavenNetworkSetup.CreateOrRefreshDemo();
            HavenNetworkSetup.ValidateDemo();
        }

        private static void BuildWindows(
            string location,
            StandaloneBuildSubtarget subtarget,
            ScriptingImplementation backend,
            BuildOptions options,
            bool useHostedContent)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Could not switch the active build target to Windows x64.");

            var namedTarget = NamedBuildTarget.Standalone;
            var previousBackend = PlayerSettings.GetScriptingBackend(namedTarget);
            var previousHybridClrEnabled = SettingsUtil.Enable;
            var hotUpdateSettings = AssetDatabase.LoadAssetAtPath<HotUpdateSettings>("Assets/Resources/HavenHotUpdateSettings.asset");
            if (useHostedContent && !hotUpdateSettings)
                throw new FileNotFoundException("Haven hot-update settings are missing.", "Assets/Resources/HavenHotUpdateSettings.asset");
            var serializedHotUpdateSettings = hotUpdateSettings ? new SerializedObject(hotUpdateSettings) : null;
            var playModeProperty = serializedHotUpdateSettings?.FindProperty("playMode");
            var primaryHostProperty = serializedHotUpdateSettings?.FindProperty("primaryHost");
            var fallbackHostProperty = serializedHotUpdateSettings?.FindProperty("fallbackHost");
            var previousPlayMode = playModeProperty?.enumValueIndex ?? -1;
            var previousPrimaryHost = primaryHostProperty?.stringValue;
            var previousFallbackHost = fallbackHostProperty?.stringValue;
            var buildPatchHost = useHostedContent ? Environment.GetEnvironmentVariable("HAVEN_PATCH_BASE_URL")?.Trim().TrimEnd('/') : null;
            if (useHostedContent && !string.IsNullOrWhiteSpace(buildPatchHost) &&
                (!Uri.TryCreate(buildPatchHost, UriKind.Absolute, out var patchUri) ||
                 (patchUri.Scheme != Uri.UriSchemeHttp && patchUri.Scheme != Uri.UriSchemeHttps) ||
                 !string.IsNullOrEmpty(patchUri.Query) || !string.IsNullOrEmpty(patchUri.Fragment)))
                throw new ArgumentException("HAVEN_PATCH_BASE_URL must be an absolute HTTP(S) URL without a query or fragment.");
            try
            {
                // The authoritative server contains only AOT code and must not be
                // rewritten to IL2CPP by HybridCLR's client-side build processor.
                SettingsUtil.Enable = backend == ScriptingImplementation.IL2CPP;
                PlayerSettings.SetScriptingBackend(namedTarget, backend);
                if (useHostedContent && playModeProperty != null)
                {
                    playModeProperty.enumValueIndex = (int)HotUpdatePlayMode.Host;
                    if (!string.IsNullOrWhiteSpace(buildPatchHost))
                    {
                        primaryHostProperty.stringValue = buildPatchHost;
                        fallbackHostProperty.stringValue = buildPatchHost;
                        Debug.Log($"[Haven] Client patch host for this build: {buildPatchHost}");
                    }
                    else
                    {
                        Debug.LogWarning("[Haven] HAVEN_PATCH_BASE_URL is not set. This client will use the configured loopback patch host and cannot download from another PC.");
                    }
                    serializedHotUpdateSettings.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssets();
                }
                var scenes = EditorBuildSettings.scenes.Where(item => item.enabled).Select(item => item.path).ToArray();
                if (scenes.Length == 0)
                    throw new InvalidOperationException("No enabled build scenes were found.");

                var fullLocation = Path.GetFullPath(location);
                Directory.CreateDirectory(Path.GetDirectoryName(fullLocation) ?? throw new InvalidOperationException("Invalid build output path."));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = fullLocation,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    subtarget = (int)subtarget,
                    options = options
                });

                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"Haven {subtarget} build failed: {report.summary.result}, errors={report.summary.totalErrors}.");
                Debug.Log($"[Haven] {subtarget} build completed: {fullLocation}");
            }
            finally
            {
                if (previousPlayMode >= 0 && playModeProperty != null)
                {
                    playModeProperty.enumValueIndex = previousPlayMode;
                    if (primaryHostProperty != null)
                        primaryHostProperty.stringValue = previousPrimaryHost;
                    if (fallbackHostProperty != null)
                        fallbackHostProperty.stringValue = previousFallbackHost;
                    serializedHotUpdateSettings.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssets();
                }
                PlayerSettings.SetScriptingBackend(namedTarget, previousBackend);
                SettingsUtil.Enable = previousHybridClrEnabled;
            }
        }
    }
}
