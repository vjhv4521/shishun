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
            var serializedHotUpdateSettings = hotUpdateSettings ? new SerializedObject(hotUpdateSettings) : null;
            var playModeProperty = serializedHotUpdateSettings?.FindProperty("playMode");
            var previousPlayMode = playModeProperty?.enumValueIndex ?? -1;
            try
            {
                // The authoritative server contains only AOT code and must not be
                // rewritten to IL2CPP by HybridCLR's client-side build processor.
                SettingsUtil.Enable = backend == ScriptingImplementation.IL2CPP;
                PlayerSettings.SetScriptingBackend(namedTarget, backend);
                if (useHostedContent && playModeProperty != null)
                {
                    playModeProperty.enumValueIndex = (int)HotUpdatePlayMode.Host;
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
                    serializedHotUpdateSettings.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssets();
                }
                PlayerSettings.SetScriptingBackend(namedTarget, previousBackend);
                SettingsUtil.Enable = previousHybridClrEnabled;
            }
        }
    }
}
