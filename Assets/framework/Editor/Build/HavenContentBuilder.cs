using System;
using System.IO;
using System.Security.Cryptography;
using Haven.Framework.HotUpdate;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace Haven.Framework.Editor
{
    public static class HavenContentBuilder
    {
        private const string PackageName = "DefaultPackage";
        private const string HotUpdateSettingsPath = "Assets/Resources/HavenHotUpdateSettings.asset";

        [MenuItem("Haven/Content/1. Build Current Assets and Publish Locally")]
        public static void BuildCurrentAssetsAndPublish()
        {
            BuildAndPublish(EBundledCopyOption.None, ResolvePackageVersion());
        }

        [MenuItem("Haven/Content/2. Compile Hotfix and Publish Locally")]
        public static void CompileHotfixAndPublish()
        {
            EnsureWindowsTarget();
            HavenFrameworkSetup.CompileHotfixOnly();
            BuildAndPublish(EBundledCopyOption.None, ResolvePackageVersion());
        }

        public static void BuildBaselineForPlayer()
        {
            BuildAndPublish(EBundledCopyOption.ClearAndCopyAll, ResolvePackageVersion());
        }

        // Entry point for -executeMethod batch mode. HAVEN_CONTENT_VERSION can override the generated version.
        public static void BuildCurrentAssetsAndPublishBatch()
        {
            BuildCurrentAssetsAndPublish();
        }

        private static void BuildAndPublish(EBundledCopyOption bundledCopyOption, string packageVersion)
        {
            EnsureWindowsTarget();
            HavenFrameworkSetup.SetupProject();

            // AssetBundle building does not require IL2CPP. On machines where only the
            // Windows Mono module is installed, Unity disables content compilation while
            // the project is configured for IL2CPP and reports an opaque configuration error.
            // Temporarily use Mono for content compilation, then restore the player's
            // intended backend so HybridCLR client builds remain IL2CPP builds.
            var namedTarget = NamedBuildTarget.Standalone;
            var previousBackend = PlayerSettings.GetScriptingBackend(namedTarget);
            try
            {
                PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.Mono2x);
                BuildAndPublishWithCurrentBackend(bundledCopyOption, packageVersion);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(namedTarget, previousBackend);
            }
        }

        private static void BuildAndPublishWithCurrentBackend(EBundledCopyOption bundledCopyOption, string packageVersion)
        {

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                              ?? throw new InvalidOperationException("Could not resolve the Unity project root.");
            var settings = AssetDatabase.LoadAssetAtPath<HotUpdateSettings>(HotUpdateSettingsPath)
                           ?? throw new FileNotFoundException("Haven hot-update settings are missing.", HotUpdateSettingsPath);
            var outputRoot = Path.Combine(projectRoot, "Build", "YooAssetBuild").Replace('\\', '/');

            // YooAsset 3.0.5's ScriptableBuildPipeline adapter calls an obsolete SBP
            // shader task which is broken by the SBP 2.6.x version bundled with Unity
            // 6.5. The supported legacy adapter produces the same YooAsset manifests
            // and runtime protocol without relying on that incompatible editor task.
            var parameters = new LegacyBuildParameters
            {
                BuildOutputRoot = outputRoot,
                BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = EBuildPipeline.LegacyBuildPipeline.ToString(),
                BuildBundleType = (int)EBundleType.AssetBundle,
                BuildTarget = BuildTarget.StandaloneWindows64,
                PackageName = PackageName,
                PackageVersion = packageVersion,
                PackageNote = "Haven local vertical-slice content",
                EnableSharePackRule = true,
                VerifyBuildingResult = true,
                FileNameStyle = EFileNameStyle.HashName,
                BundledCopyOption = bundledCopyOption,
                BundledCopyParams = string.Empty,
                CompressOption = ECompressOption.LZ4,
                ClearBuildCacheFiles = false,
                UseAssetDependencyDB = true,
                ReplaceAssetPathWithAddress = false
            };

            var result = new LegacyBuildPipeline().Run(parameters, true);
            if (!result.Success)
                throw new InvalidOperationException($"YooAsset build failed in {result.FailedTask}: {result.ErrorInfo}");

            var publishDirectory = Path.Combine(projectRoot, "Build", "LocalServer", "patches", "PC", settings.AppVersion);
            PublishDirectory(result.OutputPackageDirectory, publishDirectory, projectRoot);
            AssetDatabase.Refresh();
            Debug.Log($"[Haven] YooAsset {packageVersion} published to {publishDirectory}");
        }

        private static void PublishDirectory(string sourceDirectory, string destinationDirectory, string projectRoot)
        {
            var allowedRoot = Path.GetFullPath(Path.Combine(projectRoot, "Build", "LocalServer", "patches"));
            var destination = Path.GetFullPath(destinationDirectory);
            if (!destination.StartsWith(allowedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to publish outside {allowedRoot}.");
            if (!Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException($"YooAsset output was not found: {sourceDirectory}");
            var sourceVersion = Path.Combine(sourceDirectory, "DefaultPackage.version");
            if (!File.Exists(sourceVersion) || string.IsNullOrWhiteSpace(File.ReadAllText(sourceVersion)))
                throw new InvalidDataException("YooAsset did not produce a valid DefaultPackage.version file.");

            Directory.CreateDirectory(destination);
            foreach (var sourceFile in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
                if (relativePath.Equals("DefaultPackage.version", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.StartsWith("OutputCache", StringComparison.OrdinalIgnoreCase))
                    continue;

                var targetFile = Path.Combine(destination, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile) ?? destination);
                if (File.Exists(targetFile))
                {
                    if (!FilesMatch(sourceFile, targetFile))
                        throw new InvalidDataException($"Refusing to overwrite a published file with different content: {relativePath}");
                    continue;
                }

                var temporaryFile = targetFile + ".staging-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(sourceFile, temporaryFile);
                    File.Move(temporaryFile, targetFile);
                }
                finally
                {
                    if (File.Exists(temporaryFile))
                        File.Delete(temporaryFile);
                }
            }

            // The version pointer is promoted last; old manifests and hash-named files stay available for rollback.
            var targetVersion = Path.Combine(destination, "DefaultPackage.version");
            var stagedVersion = targetVersion + ".staging-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(sourceVersion, stagedVersion);
                if (File.Exists(targetVersion))
                    File.Replace(stagedVersion, targetVersion, targetVersion + ".previous", true);
                else
                    File.Move(stagedVersion, targetVersion);
            }
            finally
            {
                if (File.Exists(stagedVersion))
                    File.Delete(stagedVersion);
            }
        }

        private static bool FilesMatch(string left, string right)
        {
            if (new FileInfo(left).Length != new FileInfo(right).Length)
                return false;
            using var algorithm = SHA256.Create();
            using var leftStream = File.OpenRead(left);
            var leftHash = algorithm.ComputeHash(leftStream);
            using var rightStream = File.OpenRead(right);
            var rightHash = algorithm.ComputeHash(rightStream);
            for (var index = 0; index < leftHash.Length; index++)
            {
                if (leftHash[index] != rightHash[index])
                    return false;
            }
            return true;
        }

        private static string ResolvePackageVersion()
        {
            var supplied = Environment.GetEnvironmentVariable("HAVEN_CONTENT_VERSION");
            return string.IsNullOrWhiteSpace(supplied)
                ? $"{Application.version}-{DateTime.UtcNow:yyyyMMddHHmmssfff}"
                : supplied.Trim();
        }

        private static void EnsureWindowsTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64)
                return;
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Could not switch the active build target to Windows x64.");
        }
    }
}
