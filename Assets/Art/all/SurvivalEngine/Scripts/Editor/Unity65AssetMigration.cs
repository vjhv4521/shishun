using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SurvivalEngine.EditorTool
{
    /// <summary>
    /// One-time Unity 6.5 / URP migration and a reusable compatibility audit.
    /// </summary>
    public static class Unity65AssetMigration
    {
        private const string Root = "Assets/SurvivalEngine";
        private const string UrpAssetPath = Root + "/Render/URPAsset.asset";
        private const string EditorPreferenceKey = "SurvivalEngine.Unity65Migration.Completed.v2";

        private static readonly string[] CustomShaderPaths =
        {
            Root + "/Materials/FX/Grass.shader",
            Root + "/Materials/FX/GrassMobile.shader",
            Root + "/Materials/FX/Sprite-Water.shader",
            Root + "/Materials/FX/Sprites-DiffuseShadow.shader",
            Root + "/Materials/FX/Unlit-AlphaZ.shader",
            Root + "/Materials/Outline/Outline.shader",
            Root + "/Materials/UI/Sprites-UI.shader",
            Root + "/Materials/UI/UI-Top.shader",
            Root + "/Materials/UI/Text3D-URP.shader"
        };

        [InitializeOnLoadMethod]
        private static void QueueOneTimeMigration()
        {
            if (!EditorPrefs.GetBool(EditorPreferenceKey, false))
                EditorApplication.delayCall += TryRunOneTimeMigration;
        }

        private static void TryRunOneTimeMigration()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRunOneTimeMigration;
                return;
            }

            try
            {
                RunMigrationAndAudit();
                EditorPrefs.SetBool(EditorPreferenceKey, true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem("Survival Engine/Unity 6.5/Run Migration and Audit", priority = 100)]
        public static void RunMigrationAndAudit()
        {
            var report = new StringBuilder(4096);
            var problems = new List<string>();

            report.AppendLine("[UNITY65_MIGRATION_REPORT_BEGIN]");
            report.AppendLine($"Unity: {Application.unityVersion}");

            UniversalRenderPipelineAsset urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urpAsset == null)
            {
                problems.Add($"Missing URP asset: {UrpAssetPath}");
            }
            else
            {
                if (GraphicsSettings.defaultRenderPipeline != urpAsset)
                {
                    GraphicsSettings.defaultRenderPipeline = urpAsset;
                    report.AppendLine($"Assigned Graphics Settings pipeline: {UrpAssetPath}");
                }

                report.AppendLine($"Active render pipeline: {urpAsset.name}");
            }

            int upgradedMaterialCount = UpgradeBuiltInMaterials(report);
            report.AppendLine($"Built-in materials upgraded in this run: {upgradedMaterialCount}");
            int explicitlyConvertedMaterialCount = ConvertLegacyEffectMaterials(report, problems);
            report.AppendLine($"Legacy effect materials explicitly converted: {explicitlyConvertedMaterialCount}");

            AssetDatabase.SaveAssets();
            ImportCustomShaders();

            int shaderErrorCount = AuditCustomShaders(report, problems);
            int materialCount = AuditMaterials(report, problems);
            int navMeshCount = ResaveNavMeshData();
            int sceneCount = AuditAndResaveScenes(report, problems);
            int prefabCount = AuditPrefabs(report, problems);

            AssetDatabase.SaveAssets();

            report.AppendLine($"Materials audited: {materialCount}");
            report.AppendLine($"Custom shader compile errors: {shaderErrorCount}");
            report.AppendLine($"NavMeshData assets re-saved: {navMeshCount}");
            report.AppendLine($"Scenes audited/re-saved: {sceneCount}");
            report.AppendLine($"Prefabs audited: {prefabCount}");
            report.AppendLine($"Problems: {problems.Count}");
            foreach (string problem in problems)
                report.AppendLine("- " + problem);
            report.AppendLine("[UNITY65_MIGRATION_REPORT_END]");

            if (problems.Count == 0)
                Debug.Log(report.ToString());
            else
                Debug.LogError(report.ToString());
        }

        // Command-line entry point for repeatable CI or local verification.
        public static void RunFromCommandLine()
        {
            RunMigrationAndAudit();
        }

        private static int UpgradeBuiltInMaterials(StringBuilder report)
        {
            List<MaterialUpgrader> upgraders = MaterialUpgrader.FetchAllUpgradersForPipeline(typeof(UniversalRenderPipelineAsset));
            List<Material> materials = MaterialUpgrader.FetchAllUpgradableMaterialsForPipeline(typeof(UniversalRenderPipelineAsset));
            int upgraded = 0;

            foreach (Material material in materials.Where(item => item != null).Distinct())
            {
                string oldShader = material.shader != null ? material.shader.name : "<missing>";
                MaterialUpgrader.Upgrade(material, upgraders, MaterialUpgrader.UpgradeFlags.None);
                string newShader = material.shader != null ? material.shader.name : "<missing>";
                if (!string.Equals(oldShader, newShader, StringComparison.Ordinal))
                {
                    upgraded++;
                    report.AppendLine($"Material upgraded: {AssetDatabase.GetAssetPath(material)} ({oldShader} -> {newShader})");
                    EditorUtility.SetDirty(material);
                }
            }

            return upgraded;
        }

        private static void ImportCustomShaders()
        {
            foreach (string path in CustomShaderPaths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static int ConvertLegacyEffectMaterials(StringBuilder report, List<string> problems)
        {
            int converted = 0;
            converted += ConvertParticleMaterial(Root + "/Materials/FX/BubbleFX.mat", BaseShaderGUI.BlendMode.Additive, report, problems);
            converted += ConvertParticleMaterial(Root + "/Materials/FX/WaveFX.mat", BaseShaderGUI.BlendMode.Additive, report, problems);
            converted += ConvertParticleMaterial(Root + "/Materials/FX/RainCircle.mat", BaseShaderGUI.BlendMode.Alpha, report, problems);
            converted += ConvertParticleMaterial(Root + "/Materials/FX/RainDrop.mat", BaseShaderGUI.BlendMode.Alpha, report, problems);
            converted += ConvertMaterialShader(Root + "/Materials/Outline/OutlineSprite.mat", "Sprites/UI", report, problems);
            converted += ConvertMaterialShader(Root + "/Materials/UI/Text3D.mat", "Survival Engine/UI/Text3D URP", report, problems);
            return converted;
        }

        private static int ConvertParticleMaterial(
            string path,
            BaseShaderGUI.BlendMode blendMode,
            StringBuilder report,
            List<string> problems)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (material == null || shader == null)
            {
                problems.Add($"Could not convert particle material: {path}");
                return 0;
            }

            if (material.shader == shader)
                return 0;

            Texture texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            Vector2 scale = material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = material.HasProperty("_MainTex") ? material.GetTextureOffset("_MainTex") : Vector2.zero;
            Color tint = material.HasProperty("_TintColor") ? material.GetColor("_TintColor") : Color.white;
            string oldShader = material.shader != null ? material.shader.name : "<missing>";

            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", scale);
            material.SetTextureOffset("_BaseMap", offset);
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Surface", (float)BaseShaderGUI.SurfaceType.Transparent);
            material.SetFloat("_Blend", (float)blendMode);
            material.SetFloat("_Cull", (float)CullMode.Off);
            BaseShaderGUI.SetupMaterialBlendMode(material);
            ParticleGUI.SetMaterialKeywords(material);
            EditorUtility.SetDirty(material);

            report.AppendLine($"Material explicitly converted: {path} ({oldShader} -> {shader.name}, {blendMode})");
            return 1;
        }

        private static int ConvertMaterialShader(string path, string shaderName, StringBuilder report, List<string> problems)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (material == null || shader == null)
            {
                problems.Add($"Could not convert material: {path} -> {shaderName}");
                return 0;
            }

            if (material.shader == shader)
                return 0;

            string oldShader = material.shader != null ? material.shader.name : "<missing>";
            material.shader = shader;
            EditorUtility.SetDirty(material);
            report.AppendLine($"Material explicitly converted: {path} ({oldShader} -> {shaderName})");
            return 1;
        }

        private static int AuditCustomShaders(StringBuilder report, List<string> problems)
        {
            int errors = 0;
            foreach (string path in CustomShaderPaths)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                {
                    errors++;
                    problems.Add($"Shader failed to import: {path}");
                    continue;
                }

                var messages = ShaderUtil.GetShaderMessages(shader);
                var shaderErrors = messages
                    .Where(message => message.severity == ShaderCompilerMessageSeverity.Error)
                    .ToArray();

                if (!shader.isSupported)
                    problems.Add($"Shader is unsupported on the current graphics device: {path}");

                foreach (var message in shaderErrors)
                {
                    errors++;
                    problems.Add($"{path}:{message.line} [{message.platform}] {message.message}");
                }

                report.AppendLine($"Shader: {shader.name}, supported={shader.isSupported}, errors={shaderErrors.Length}");
            }

            return errors;
        }

        private static int AuditMaterials(StringBuilder report, List<string> problems)
        {
            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { Root });
            var shaderUsage = new SortedDictionary<string, int>(StringComparer.Ordinal);

            foreach (string guid in materialGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    problems.Add($"Material failed to load: {path}");
                    continue;
                }

                if (material.shader == null)
                {
                    problems.Add($"Material has a missing shader: {path}");
                    continue;
                }

                string shaderName = material.shader.name;
                shaderUsage.TryGetValue(shaderName, out int count);
                shaderUsage[shaderName] = count + 1;

            }

            report.AppendLine("Material shader usage:");
            foreach (KeyValuePair<string, int> usage in shaderUsage.OrderByDescending(item => item.Value).ThenBy(item => item.Key))
                report.AppendLine($"- {usage.Key}: {usage.Value}");

            return materialGuids.Length;
        }

        private static int ResaveNavMeshData()
        {
            string[] navMeshGuids = AssetDatabase.FindAssets("t:NavMeshData", new[] { Root + "/Scenes" });
            foreach (string guid in navMeshGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                NavMeshData data = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
                if (data != null)
                    EditorUtility.SetDirty(data);
            }

            return navMeshGuids.Length;
        }

        private static int AuditAndResaveScenes(StringBuilder report, List<string> problems)
        {
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { Root + "/Scenes" });
            foreach (string guid in sceneGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Scene existingScene = SceneManager.GetSceneByPath(path);
                bool wasAlreadyOpen = existingScene.IsValid() && existingScene.isLoaded;
                Scene scene = wasAlreadyOpen ? existingScene : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

                int missingScripts = CountMissingScripts(scene.GetRootGameObjects());
                if (missingScripts > 0)
                    problems.Add($"Scene has {missingScripts} missing script(s): {path}");

                if (!wasAlreadyOpen)
                {
                    EditorSceneManager.SaveScene(scene);
                    EditorSceneManager.CloseScene(scene, true);
                }

                report.AppendLine($"Scene: {path}, missingScripts={missingScripts}");
            }

            return sceneGuids.Length;
        }

        private static int AuditPrefabs(StringBuilder report, List<string> problems)
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { Root });
            int prefabsWithMissingScripts = 0;

            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(path);
                    int missingScripts = CountMissingScripts(new[] { root });
                    if (missingScripts > 0)
                    {
                        prefabsWithMissingScripts++;
                        problems.Add($"Prefab has {missingScripts} missing script(s): {path}");
                    }
                }
                catch (Exception exception)
                {
                    problems.Add($"Prefab failed to audit: {path} ({exception.Message})");
                }
                finally
                {
                    if (root != null)
                        PrefabUtility.UnloadPrefabContents(root);
                }
            }

            report.AppendLine($"Prefabs with missing scripts: {prefabsWithMissingScripts}");
            return prefabGuids.Length;
        }

        private static int CountMissingScripts(IEnumerable<GameObject> roots)
        {
            int count = 0;
            foreach (GameObject root in roots)
            {
                if (root == null)
                    continue;

                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                    count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            }

            return count;
        }
    }
}
