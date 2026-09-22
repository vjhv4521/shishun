using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using HybridCLR.Editor;
using Haven.Framework.HotUpdate;
using Haven.Networking;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Haven.Framework.Editor
{
    public static class HavenPlayerBuilder
    {
        private enum ClientContentMode
        {
            None,
            Offline,
            Hosted
        }

        [MenuItem("Haven/Build/Windows Dedicated Server")]
        public static void BuildWindowsDedicatedServer()
        {
            PrepareScene();
            BuildWindows(
                "Build/WindowsServer/HavenServer.exe",
                StandaloneBuildSubtarget.Server,
                ScriptingImplementation.Mono2x,
                BuildOptions.Development,
                ClientContentMode.None);
        }

        [MenuItem("Haven/Build/Windows Headless Server (module fallback)")]
        public static void BuildWindowsHeadlessServer()
        {
            PrepareScene();
            BuildWindows(
                "Build/WindowsServerFallback/HavenServer.exe",
                StandaloneBuildSubtarget.Player,
                ScriptingImplementation.Mono2x,
                BuildOptions.Development,
                ClientContentMode.None);
        }

        [MenuItem("Haven/Build/Windows Client Offline (HybridCLR)")]
        public static void BuildWindowsOfflineClient()
        {
            BuildWindowsClient(ClientContentMode.Offline);
        }

        [MenuItem("Haven/Build/Windows Client Hosted (HybridCLR)")]
        public static void BuildWindowsHostedClient()
        {
            ValidateHostedReleaseEnvironment();
            BuildWindowsClient(ClientContentMode.Hosted);
        }

        public static void BuildWindowsClient()
        {
            BuildWindowsOfflineClient();
        }

        private static void BuildWindowsClient(ClientContentMode mode)
        {
            PrepareScene();
            HavenFrameworkSetup.GenerateAllAndPrepareAssets();
            // Keep the remote pointer unchanged until the corresponding player has
            // actually been created. A failed Hosted build must never become the
            // version advertised by the gateway.
            HavenContentBuilder.BuildBaselineForPlayer(false);
            BuildWindows(
                "Build/WindowsClient/HavenClient.exe",
                StandaloneBuildSubtarget.Player,
                ScriptingImplementation.IL2CPP,
                BuildOptions.Development | BuildOptions.AllowDebugging,
                mode);
            if (mode == ClientContentMode.Hosted)
            {
                HavenContentBuilder.PublishBuiltPackage(
                    Environment.GetEnvironmentVariable("HAVEN_CONTENT_VERSION")?.Trim());
            }
        }

        // Entry points for -executeMethod batch mode.
        public static void BuildWindowsDedicatedServerBatch() => BuildWindowsDedicatedServer();
        public static void BuildWindowsHeadlessServerBatch() => BuildWindowsHeadlessServer();
        public static void BuildWindowsClientBatch() => BuildWindowsOfflineClient();
        public static void BuildWindowsHostedClientBatch() => BuildWindowsHostedClient();

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
            ClientContentMode clientMode)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Could not switch the active build target to Windows x64.");

            var namedTarget = NamedBuildTarget.Standalone;
            var previousBackend = PlayerSettings.GetScriptingBackend(namedTarget);
            var previousHybridClrEnabled = SettingsUtil.Enable;
            var windowsPlatformName = BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneWindows64);
            var previousCreateSolution = EditorUserBuildSettings.GetPlatformSettings(windowsPlatformName, "CreateSolution");
            var hotUpdateSettings = AssetDatabase.LoadAssetAtPath<HotUpdateSettings>("Assets/Resources/HavenHotUpdateSettings.asset");
            var networkSettings = AssetDatabase.LoadAssetAtPath<HavenNetworkSettings>("Assets/Resources/HavenNetworkSettings.asset");
            if (clientMode != ClientContentMode.None && !hotUpdateSettings)
                throw new FileNotFoundException("Haven hot-update settings are missing.", "Assets/Resources/HavenHotUpdateSettings.asset");
            if (clientMode == ClientContentMode.Hosted && !networkSettings)
                throw new FileNotFoundException("Haven network settings are missing.", "Assets/Resources/HavenNetworkSettings.asset");
            var serializedHotUpdateSettings = hotUpdateSettings ? new SerializedObject(hotUpdateSettings) : null;
            var serializedNetworkSettings = networkSettings ? new SerializedObject(networkSettings) : null;
            var playModeProperty = serializedHotUpdateSettings?.FindProperty("playMode");
            var primaryHostProperty = serializedHotUpdateSettings?.FindProperty("primaryHost");
            var fallbackHostProperty = serializedHotUpdateSettings?.FindProperty("fallbackHost");
            var defaultGameHostProperty = serializedNetworkSettings?.FindProperty("defaultHost");
            var previousPlayMode = playModeProperty?.enumValueIndex ?? -1;
            var previousPrimaryHost = primaryHostProperty?.stringValue;
            var previousFallbackHost = fallbackHostProperty?.stringValue;
            var previousGameHost = defaultGameHostProperty?.stringValue;
            var buildPatchHost = clientMode == ClientContentMode.Hosted
                ? Environment.GetEnvironmentVariable("HAVEN_PATCH_BASE_URL")?.Trim().TrimEnd('/')
                : null;
            var buildGameHost = clientMode == ClientContentMode.Hosted
                ? Environment.GetEnvironmentVariable("HAVEN_GAME_SERVER_HOST")?.Trim()
                : null;
            try
            {
                // The authoritative server contains only AOT code and must not be
                // rewritten to IL2CPP by HybridCLR's client-side build processor.
                SettingsUtil.Enable = backend == ScriptingImplementation.IL2CPP;
                PlayerSettings.SetScriptingBackend(namedTarget, backend);
                // Unity 6 remembers "Create Visual Studio Solution" as a per-platform
                // editor preference. Batch builds must override it or BuildPlayer can
                // report success while producing a C++ solution instead of the requested
                // Windows executable.
                EditorUserBuildSettings.SetPlatformSettings(windowsPlatformName, "CreateSolution", "false");
                if (clientMode != ClientContentMode.None && playModeProperty != null)
                {
                    if (clientMode == ClientContentMode.Hosted)
                    {
                        playModeProperty.enumValueIndex = (int)HotUpdatePlayMode.Host;
                        primaryHostProperty.stringValue = buildPatchHost;
                        fallbackHostProperty.stringValue = buildPatchHost;
                        defaultGameHostProperty.stringValue = buildGameHost;
                        serializedNetworkSettings.ApplyModifiedPropertiesWithoutUndo();
                        Debug.Log($"[Haven] Hosted client endpoints: patches={buildPatchHost}, game={buildGameHost}:{networkSettings.Port}");
                    }
                    else
                    {
                        playModeProperty.enumValueIndex = (int)HotUpdatePlayMode.Offline;
                        Debug.Log("[Haven] Building a self-contained Offline client from the bundled baseline.");
                    }
                    serializedHotUpdateSettings.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssets();
                }
                var scenes = clientMode == ClientContentMode.None
                    ? new[] { HavenNetworkSetup.ScenePath, HavenNetworkSetup.WorldScenePath }
                    : EditorBuildSettings.scenes.Where(item => item.enabled).Select(item => item.path).ToArray();
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
                    extraScriptingDefines = clientMode == ClientContentMode.None && subtarget == StandaloneBuildSubtarget.Player
                        ? new[] { "HAVEN_SERVER_BUILD" }
                        : Array.Empty<string>(),
                    options = options
                });

                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"Haven {subtarget} build failed: {report.summary.result}, errors={report.summary.totalErrors}.");
                if (!File.Exists(fullLocation))
                    throw new BuildFailedException(
                        $"Haven {subtarget} build reported success but did not create the requested executable: {fullLocation}");
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
                }
                if (defaultGameHostProperty != null && previousGameHost != null)
                {
                    defaultGameHostProperty.stringValue = previousGameHost;
                    serializedNetworkSettings.ApplyModifiedPropertiesWithoutUndo();
                }
                AssetDatabase.SaveAssets();
                EditorUserBuildSettings.SetPlatformSettings(windowsPlatformName, "CreateSolution", previousCreateSolution);
                PlayerSettings.SetScriptingBackend(namedTarget, previousBackend);
                SettingsUtil.Enable = previousHybridClrEnabled;
            }
        }

        private static void ValidateHostedReleaseEnvironment()
        {
            var contentVersion = Environment.GetEnvironmentVariable("HAVEN_CONTENT_VERSION")?.Trim();
            if (string.IsNullOrWhiteSpace(contentVersion))
                throw new InvalidOperationException("Hosted release builds require an explicit HAVEN_CONTENT_VERSION.");
            if (!contentVersion.StartsWith(Application.version + "-", StringComparison.Ordinal) ||
                contentVersion.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException($"HAVEN_CONTENT_VERSION must start with '{Application.version}-' and be safe as a file name.");

            var patchHost = Environment.GetEnvironmentVariable("HAVEN_PATCH_BASE_URL")?.Trim().TrimEnd('/');
            if (!Uri.TryCreate(patchHost, UriKind.Absolute, out var patchUri) ||
                (patchUri.Scheme != Uri.UriSchemeHttp && patchUri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(patchUri.Query) || !string.IsNullOrEmpty(patchUri.Fragment) ||
                patchUri.IsLoopback || !IsPrivateIpv4(patchUri.Host))
                throw new InvalidOperationException("HAVEN_PATCH_BASE_URL must use a non-loopback private IPv4 HTTP(S) endpoint without a query or fragment.");

            var gameHost = Environment.GetEnvironmentVariable("HAVEN_GAME_SERVER_HOST")?.Trim();
            if (!IsPrivateIpv4(gameHost))
                throw new InvalidOperationException("HAVEN_GAME_SERVER_HOST must be a non-loopback private IPv4 address.");
            if (!string.Equals(patchUri.Host, gameHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The patch and game server hosts must use the same private IPv4 address for the LAN release.");
        }

        private static bool IsPrivateIpv4(string value)
        {
            if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                return false;
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168;
        }
    }
}
