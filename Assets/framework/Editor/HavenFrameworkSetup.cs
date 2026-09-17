using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Haven.Framework.HotUpdate;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Installer;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEditorInternal;
using UnityEngine;
using YooAsset.Editor;

namespace Haven.Framework.Editor
{
    public static class HavenFrameworkSetup
    {
        private const string HotfixAsmdefPath = "Assets/Hotfix/Haven.Hotfix.asmdef";
        private const string GeneratedPath = "Assets/Hotfix/Generated";
        private const string ContentPath = "Assets/Hotfix/Content";
        private const string ResourcesPath = "Assets/Resources";
        private const string SettingsAssetPath = ResourcesPath + "/HavenHotUpdateSettings.asset";
        private const string PackageName = "DefaultPackage";

        [MenuItem("Haven/Framework/1. Setup Project")]
        public static void SetupProject()
        {
            EnsureFolders();
            EnsureRuntimeSettings();
            ConfigureHybridClr();
            ConfigureYooAssetCollectors();
            ConfigurePlayerSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Haven] Framework project setup completed.");
        }

        [MenuItem("Haven/Framework/2. Install HybridCLR Runtime")]
        public static void InstallHybridClrRuntime()
        {
            SetupProject();
            var installer = new InstallerController();
            if (installer.HasInstalledHybridCLR())
            {
                Debug.Log("[Haven] HybridCLR runtime is already installed.");
                return;
            }
            installer.InstallDefaultHybridCLR();
            Debug.Log("[Haven] HybridCLR runtime installation completed.");
        }

        [MenuItem("Haven/Framework/3. Generate All and Prepare DLL Assets")]
        public static void GenerateAllAndPrepareAssets()
        {
            SetupProject();
            var installer = new InstallerController();
            if (!installer.HasInstalledHybridCLR())
                throw new InvalidOperationException("HybridCLR is not installed. Run Haven/Framework/2. Install HybridCLR Runtime first.");

            PrebuildCommand.GenerateAll();
            CopyGeneratedAssemblies(includeAotMetadata: true);
            Debug.Log("[Haven] HybridCLR generation and DLL asset preparation completed.");
        }

        [MenuItem("Haven/Framework/Compile Hotfix DLL Only")]
        public static void CompileHotfixOnly()
        {
            SetupProject();
            CompileDllCommand.CompileDll(EditorUserBuildSettings.activeBuildTarget, EditorUserBuildSettings.development);
            CopyGeneratedAssemblies(includeAotMetadata: false);
            Debug.Log("[Haven] Hotfix DLL compilation completed.");
        }

        [MenuItem("Haven/Framework/Validate Project")]
        public static void ValidateProject()
        {
            var errors = CollectValidationErrors();
            if (errors.Count == 0)
            {
                Debug.Log("[Haven] Framework validation passed.");
                return;
            }

            foreach (var error in errors)
                Debug.LogError("[Haven] " + error);
            throw new InvalidOperationException($"Haven framework validation failed with {errors.Count} error(s).");
        }

        // Entry points for -executeMethod batch mode.
        public static void SetupProjectBatch()
        {
            SetupProject();
            ValidateProject();
        }

        public static void InstallAndSetupBatch()
        {
            InstallHybridClrRuntime();
            ValidateProject();
        }

        private static void EnsureFolders()
        {
            EnsureFolder(ResourcesPath);
            EnsureFolder(GeneratedPath);
            EnsureFolder(ContentPath);
        }

        private static void EnsureFolder(string assetPath)
        {
            var parts = assetPath.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static void EnsureRuntimeSettings()
        {
            var asset = AssetDatabase.LoadAssetAtPath<HotUpdateSettings>(SettingsAssetPath);
            if (!asset)
            {
                asset = ScriptableObject.CreateInstance<HotUpdateSettings>();
                AssetDatabase.CreateAsset(asset, SettingsAssetPath);
            }
            var metadata = MergeDistinct(asset.AotMetadataLocations, new[] { "UnityEngine.JSONSerializeModule.dll" });
            var serialized = new SerializedObject(asset);
            var locations = serialized.FindProperty("aotMetadataLocations");
            locations.arraySize = metadata.Length;
            for (var index = 0; index < metadata.Length; index++) locations.GetArrayElementAtIndex(index).stringValue = metadata[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureHybridClr()
        {
            var asmdef = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(HotfixAsmdefPath);
            if (!asmdef)
                throw new FileNotFoundException("Hotfix asmdef not found.", HotfixAsmdefPath);

            var settings = HybridCLRSettings.Instance;
            settings.enable = true;
            settings.hybridclrRepoURL = "https://github.com/focus-creative-games/hybridclr";
            settings.il2cppPlusRepoURL = "https://github.com/focus-creative-games/il2cpp_plus";
            settings.hotUpdateAssemblyDefinitions = new[] { asmdef };
            settings.preserveHotUpdateAssemblies = Array.Empty<string>();
            settings.patchAOTAssemblies = MergeDistinct(
                settings.patchAOTAssemblies,
                new[] { "mscorlib", "System", "System.Core", "Haven.Framework", "UnityEngine.JSONSerializeModule" });
            HybridCLRSettings.Save();
        }

        private static void ConfigureYooAssetCollectors()
        {
            var setting = BundleCollectorSettingData.Setting;
            var package = setting.Packages.FirstOrDefault(item => item.PackageName == PackageName)
                          ?? BundleCollectorSettingData.CreatePackage(PackageName);
            package.EnableAddressable = true;
            package.SupportExtensionless = true;

            var hotfixGroup = package.Groups.FirstOrDefault(item => item.GroupName == "HotUpdate")
                              ?? BundleCollectorSettingData.CreateGroup(package, "HotUpdate");
            hotfixGroup.AssetTags = "hotupdate";
            UpsertCollector(hotfixGroup, GeneratedPath, nameof(AddressByFileName), nameof(PackRawFile), "hotupdate");

            var contentGroup = package.Groups.FirstOrDefault(item => item.GroupName == "Content")
                               ?? BundleCollectorSettingData.CreateGroup(package, "Content");
            contentGroup.AssetTags = "content";
            UpsertCollector(contentGroup, ContentPath, nameof(AddressByFileName), nameof(PackDirectory), "content");

            BundleCollectorSettingData.ModifyPackage(package);
            BundleCollectorSettingData.SaveFile();
        }

        private static void UpsertCollector(BundleCollectorGroup group, string collectPath, string addressRule, string packRule, string tags)
        {
            var collector = group.Collectors.FirstOrDefault(item => item.CollectPath == collectPath);
            if (collector == null)
            {
                collector = new BundleCollector();
                BundleCollectorSettingData.CreateCollector(group, collector);
            }

            collector.CollectPath = collectPath;
            collector.CollectorGUID = AssetDatabase.AssetPathToGUID(collectPath);
            collector.CollectorType = ECollectorType.MainAssetCollector;
            collector.AddressRuleName = addressRule;
            collector.PackRuleName = packRule;
            collector.FilterRuleName = nameof(CollectAll);
            collector.AssetTags = tags;
            BundleCollectorSettingData.ModifyCollector(group, collector);
        }

        private static void ConfigurePlayerSettings()
        {
            var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);
            PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.IL2CPP);
            // HybridCLR requires the full .NET Framework profile on Unity 2021+.
            PlayerSettings.SetApiCompatibilityLevel(namedTarget, ApiCompatibilityLevel.NET_Unity_4_8);
        }

        private static void CopyGeneratedAssemblies(bool includeAotMetadata)
        {
            EnsureFolders();
            var target = EditorUserBuildSettings.activeBuildTarget;
            var hotfixSource = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            foreach (var assemblyName in SettingsUtil.HotUpdateAssemblyNamesExcludePreserved)
                CopyAssembly(hotfixSource, assemblyName, required: true);

            if (includeAotMetadata)
            {
                var aotSource = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
                foreach (var assemblyName in SettingsUtil.AOTAssemblyNames)
                    CopyAssembly(aotSource, assemblyName, required: true);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        private static void CopyAssembly(string sourceDirectory, string assemblyName, bool required)
        {
            var source = Path.Combine(sourceDirectory, assemblyName + ".dll");
            if (!File.Exists(source))
            {
                if (required)
                    throw new FileNotFoundException($"Generated assembly not found: {source}", source);
                return;
            }

            var destination = Path.Combine(GeneratedPath, assemblyName + ".dll.bytes");
            File.Copy(source, destination, true);
        }

        private static List<string> CollectValidationErrors()
        {
            var errors = new List<string>();
            if (!AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(HotfixAsmdefPath))
                errors.Add("Missing Haven.Hotfix.asmdef.");
            if (!AssetDatabase.LoadAssetAtPath<HotUpdateSettings>(SettingsAssetPath))
                errors.Add("Missing Assets/Resources/HavenHotUpdateSettings.asset.");

            var hybridSettings = HybridCLRSettings.Instance;
            if (!hybridSettings.enable)
                errors.Add("HybridCLR is disabled.");
            if (hybridSettings.hotUpdateAssemblyDefinitions == null ||
                !hybridSettings.hotUpdateAssemblyDefinitions.Any(item => item && item.name == "Haven.Hotfix"))
                errors.Add("Haven.Hotfix is not registered as a hot update assembly.");

            var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);
            if (PlayerSettings.GetScriptingBackend(namedTarget) != ScriptingImplementation.IL2CPP)
                errors.Add("Active build target is not configured for IL2CPP.");

            try
            {
                var package = BundleCollectorSettingData.Setting.GetPackage(PackageName);
                if (!package.EnableAddressable)
                    errors.Add("YooAsset DefaultPackage addressable locations are disabled.");
                if (!package.Groups.SelectMany(item => item.Collectors).Any(item => item.CollectPath == GeneratedPath && item.PackRuleName == nameof(PackRawFile)))
                    errors.Add("YooAsset raw-file collector for generated DLLs is missing.");
            }
            catch (Exception exception)
            {
                errors.Add("YooAsset collector configuration is invalid: " + exception.Message);
            }

            return errors;
        }

        private static string[] MergeDistinct(IEnumerable<string> current, IEnumerable<string> required)
        {
            return (current ?? Array.Empty<string>())
                .Concat(required ?? Array.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }
}
