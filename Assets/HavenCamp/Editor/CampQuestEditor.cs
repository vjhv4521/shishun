using System;
using System.IO;
using System.Linq;
using Haven.Framework.Editor;
using Haven.Framework.Bootstrap;
using Haven.Framework.HotUpdate;
using HybridCLR.Editor;
using SurvivalEngine;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

namespace Haven.Camp.Editor
{
    public static class CampQuestEditor
    {
        public const string ScenePath = "Assets/Scenes/WorldGenMap.unity";
        private const string Art = "Assets/Art/all/SurvivalEngine/";
        private const string RootName = "[Haven Camp Quests]";
        private static readonly Vector3 StewardOffset = new Vector3(-3f, 0f, -1f);

        [MenuItem("Haven/Camp Quests/1. Prepare WorldGenMap")]
        public static void SetupScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureTmp();
            var font = EnsureFont();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var existingInstaller = Object.FindAnyObjectByType<CampQuestInstaller>();
            if (existingInstaller)
            {
                EnsureSceneBootstrap(existingInstaller.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                ValidateScene();
                return;
            }
            var player = Object.FindAnyObjectByType<PlayerCharacter>();
            if (!player || !Object.FindAnyObjectByType<TheGame>()) throw new InvalidOperationException("WorldGenMap needs its player and Managers prefab.");
            var root = new GameObject(RootName);
            EnsureSceneBootstrap(root);
            var npcPrefab = CreateStewardPrefab(font);
            var npc = (GameObject)PrefabUtility.InstantiatePrefab(npcPrefab, root.transform);
            PositionSteward(npc.transform, player.transform);
            var panelObject = new GameObject("Camp Quest UI", typeof(RectTransform));
            panelObject.transform.SetParent(root.transform, false);
            panelObject.AddComponent<CampQuestPanel>().Configure(font);
            var installer = root.AddComponent<CampQuestInstaller>();
            installer.Configure(npc.transform, AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Hotfix/Content/CampQuestCatalog.json"));
            AddSupplies(root.transform, player.transform.position, "wood", 5, 2);
            AddSupplies(root.transform, player.transform.position, "rock", 4, -2);
            root.AddComponent<CampQuestSmokeRunner>();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the camp quest scene.");
            AssetDatabase.SaveAssets();
            ValidateScene();
        }

        private static void EnsureSceneBootstrap(GameObject root)
        {
            var bootstrap = root.GetComponent<GameBootstrap>() ?? root.AddComponent<GameBootstrap>();
            var serialized = new SerializedObject(bootstrap);
            // The legacy Load/New buttons reload this scene. Its services must be rebuilt with it.
            serialized.FindProperty("persistAcrossScenes").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Haven/Camp Quests/4. Reposition Steward")]
        public static void RepositionSteward()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var player = Object.FindAnyObjectByType<PlayerCharacter>();
            var steward = Object.FindAnyObjectByType<CampQuestSteward>();
            if (!player || !steward) throw new InvalidOperationException("WorldGenMap needs its player and camp steward.");
            var previous = steward.transform.position;
            PositionSteward(steward.transform, player.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the repositioned camp steward.");
            ValidateScene();
            Debug.Log($"[Haven Camp] Steward moved from {previous} to {steward.transform.position}.");
        }

        private static void PositionSteward(Transform steward, Transform player)
        {
            // The former +X/+Z position overlaps the nearby TreePine in WorldGenMap.
            var destination = Grounded(player.position + StewardOffset);
            var nearbyTree = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.name.StartsWith("Tree", StringComparison.Ordinal) &&
                    new Vector2(candidate.position.x - destination.x, candidate.position.z - destination.z).sqrMagnitude < 2.5f * 2.5f);
            if (nearbyTree) throw new InvalidOperationException("Steward destination is too close to " + nearbyTree.name);
            steward.position = destination;
            var towardPlayer = player.position - destination;
            towardPlayer.y = 0;
            if (towardPlayer.sqrMagnitude > 0.01f) steward.rotation = Quaternion.LookRotation(towardPlayer);
        }

        // Batch-only smoke entry; test flags isolate the save slot in CampQuestSmokeRunner.
        public static void PlaySmoke()
        {
            SetupScene();
            if (!Application.isBatchMode || !Environment.GetCommandLineArgs().Contains("-campQuestSmoke"))
                throw new InvalidOperationException("PlaySmoke requires -batchmode -campQuestSmoke.");
            var settings = AssetDatabase.LoadAssetAtPath<HotUpdateSettings>("Assets/Resources/HavenHotUpdateSettings.asset");
            if (settings.PlayMode != HotUpdatePlayMode.EditorDirect) throw new InvalidOperationException("Use EditorDirect for the editor smoke check.");
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Haven/Camp Quests/2. Validate Scene")]
        public static void ValidateScene()
        {
            if (!Object.FindAnyObjectByType<CampQuestInstaller>() || !Object.FindAnyObjectByType<CampQuestSteward>() ||
                !Object.FindAnyObjectByType<CampQuestPanel>()) throw new InvalidOperationException("Camp scene is missing its installer, steward or panel.");
            foreach (var id in new[] { "wood", "rock", "bread" })
                if (!Resources.LoadAll<ItemData>("Items").Any(item => item.id == id)) throw new InvalidOperationException("Missing item: " + id);
            foreach (var sceneRoot in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var transform in sceneRoot.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                        throw new InvalidOperationException("Missing script on " + transform.name);
            Debug.Log("[Haven Camp] Scene configuration validated.");
        }

        [MenuItem("Haven/Build/Windows Camp Quest Demo")]
        public static void BuildDemo()
        {
            SetupScene();
            CampQuestAdapterChecks.Run();
            var settings = AssetDatabase.LoadAssetAtPath<HotUpdateSettings>("Assets/Resources/HavenHotUpdateSettings.asset");
            var serialized = new SerializedObject(settings);
            var previousMode = serialized.FindProperty("playMode").enumValueIndex;
            var previousBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            var previousHybrid = SettingsUtil.Enable;
            var previousSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                    throw new BuildFailedException("Switch the active build target to Windows x64 before building the camp demo.");
                // CompilePlayerScripts inherits this setting, even when BuildPlayer later specifies Player.
                // A lingering Dedicated Server target otherwise compiles hotfix DLLs against the wrong .NET profile.
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
                SettingsUtil.Enable = true;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
                HavenFrameworkSetup.GenerateAllAndPrepareAssets();
                HavenContentBuilder.BuildBaselineForPlayer(false);
                serialized.Update();
                serialized.FindProperty("playMode").enumValueIndex = (int)HotUpdatePlayMode.Offline;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                var output = "Build/WindowsCampQuest/HavenCamp.exe";
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = output,
                    target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone,
                    subtarget = (int)StandaloneBuildSubtarget.Player,
                    options = BuildOptions.Development | BuildOptions.AllowDebugging
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("Camp quest demo build failed: " + report.summary.totalErrors);
                Debug.Log("[Haven Camp] Offline Windows demo built: " + Path.GetFullPath(output));
            }
            finally
            {
                serialized.Update();
                serialized.FindProperty("playMode").enumValueIndex = previousMode;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                SettingsUtil.Enable = previousHybrid;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, previousBackend);
                EditorUserBuildSettings.standaloneBuildSubtarget = previousSubtarget;
            }
        }

        private static void EnsureTmp()
        {
            const string settingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
            if (!AssetDatabase.LoadAssetAtPath<TMP_Settings>(settingsPath) || !Shader.Find("TextMeshPro/Distance Field"))
                throw new InvalidOperationException("Import Window > TextMeshPro > Import TMP Essential Resources before preparing the camp scene.");
            // Remove only the temporary settings created during initial camp setup; keep one resource by this name.
            const string temporarySettings = "Assets/HavenCamp/Resources/TMP Settings.asset";
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(temporarySettings)) AssetDatabase.DeleteAsset(temporarySettings);
        }

        private static TMP_FontAsset EnsureFont()
        {
            const string assetPath = "Assets/HavenCamp/Fonts/HavenChinese.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing) return existing;
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/HavenCamp/Fonts/NotoSansCJKsc-Regular.otf");
            if (!source) throw new FileNotFoundException("Noto Chinese font has not been imported.");
            var font = TMP_FontAsset.CreateFontAsset(source, 60, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            font.name = "HavenChinese";
            AssetDatabase.CreateAsset(font, assetPath);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
            TMP_Settings.defaultFontAsset = font;
            EditorUtility.SetDirty(TMP_Settings.instance);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            return font;
        }

        private static GameObject CreateStewardPrefab(TMP_FontAsset font)
        {
            const string path = "Assets/HavenCamp/Prefabs/CampSteward.prefab";
            Directory.CreateDirectory("Assets/HavenCamp/Prefabs");
            var root = new GameObject("营地管事");
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Prefabs/PlayerCharacterGirl.prefab");
            var source = playerPrefab.GetComponentInChildren<Animator>(true);
            if (!source) throw new InvalidOperationException("Player model has no animator.");
            var model = Object.Instantiate(source.gameObject, root.transform);
            model.name = "Steward Visual";
            model.transform.localPosition = Vector3.zero;
            foreach (var script in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
            var animator = model.GetComponent<Animator>();
            animator.applyRootMotion = false;
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0, 0.85f, 0);
            capsule.height = 1.7f;
            capsule.radius = 0.35f;
            var selectable = root.AddComponent<Selectable>();
            selectable.type = SelectableType.Interact;
            selectable.use_range = 2.5f;
            selectable.actions = Array.Empty<SAction>();
            selectable.groups = Array.Empty<GroupData>();
            selectable.always_run_scripts = true;
            root.AddComponent<CampQuestSteward>();
            var labelObject = new GameObject("Steward name");
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = new Vector3(0, 2.3f, 0);
            var label = labelObject.AddComponent<TextMeshPro>();
            label.font = font;
            label.text = "营地管事";
            label.fontSize = 3;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 0.88f, 0.55f);
            label.rectTransform.sizeDelta = new Vector2(5, 1);
            labelObject.AddComponent<CampQuestNameplate>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void AddSupplies(Transform root, Vector3 origin, string id, int count, float side)
        {
            var data = Resources.LoadAll<ItemData>("Items").Single(item => item.id == id);
            for (var index = 0; index < count; index++)
            {
                var item = (GameObject)PrefabUtility.InstantiatePrefab(data.item_prefab.gameObject, root);
                item.name = "Camp supply " + id + " " + index;
                item.transform.position = Grounded(origin + new Vector3(side, 0, 3f + index * 1.25f));
                item.GetComponent<Item>().quantity = 2;
                item.GetComponent<UniqueID>().unique_id = "haven-camp-supply-" + id + "-" + index;
            }
        }

        private static Vector3 Grounded(Vector3 point)
        {
            if (NavMesh.SamplePosition(point, out var hit, 12f, NavMesh.AllAreas)) return hit.position;
            if (Physics.Raycast(point + Vector3.up * 20, Vector3.down, out var floor, 50f, 1 << 9)) return floor.point;
            throw new InvalidOperationException("Could not find walkable ground near the camp spawn.");
        }
    }
}
