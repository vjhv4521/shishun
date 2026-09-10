using System;
using System.Linq;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using Haven.Framework.Demo;
using Haven.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Haven.Framework.Editor
{
    public static class HavenNetworkSetup
    {
        public const string ScenePath = "Assets/Scenes/FrameworkDemo.unity";
        public const string PlayerPrefabPath = "Assets/Prefabs/Network/HavenPlayer.prefab";
        public const string NetworkSettingsPath = "Assets/Resources/HavenNetworkSettings.asset";
        private const string DefaultPrefabsPath = "Assets/DefaultPrefabObjects.asset";

        [MenuItem("Haven/Network/1. Create or Refresh Demo")]
        public static void CreateOrRefreshDemo()
        {
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/Network");
            EnsureFolder("Assets/Resources");

            var settings = EnsureNetworkSettings();
            var player = EnsurePlayerPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AddPlayerToDefaultPrefabs(player);
            CreateDemoScene(settings, player);
            RegisterScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[Haven] FishNet demo is ready: {ScenePath}");
        }

        [MenuItem("Haven/Network/Validate Demo")]
        public static void ValidateDemo()
        {
            var settings = AssetDatabase.LoadAssetAtPath<HavenNetworkSettings>(NetworkSettingsPath);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            var prefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabsPath);

            if (!settings || !player || !scene || !prefabs)
                throw new InvalidOperationException("Haven network demo is incomplete. Run Haven/Network/1. Create or Refresh Demo.");
            if (!player.TryGetComponent<NetworkObject>(out var networkObject) ||
                !player.TryGetComponent<FishNetPlayerAvatar>(out _) ||
                !player.TryGetComponent<NetworkTransform>(out _))
                throw new InvalidOperationException("HavenPlayer prefab is missing required FishNet components.");
            if (!prefabs.Prefabs.Contains(networkObject))
                throw new InvalidOperationException("HavenPlayer is not registered in FishNet DefaultPrefabObjects.");
            if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == ScenePath))
                throw new InvalidOperationException("FrameworkDemo scene is not enabled in build settings.");

            Debug.Log("[Haven] FishNet demo validation passed.");
        }

        // Entry point for -executeMethod batch mode.
        public static void SetupNetworkDemoBatch()
        {
            CreateOrRefreshDemo();
            ValidateDemo();
        }

        private static HavenNetworkSettings EnsureNetworkSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<HavenNetworkSettings>(NetworkSettingsPath);
            if (settings)
                return settings;
            settings = ScriptableObject.CreateInstance<HavenNetworkSettings>();
            AssetDatabase.CreateAsset(settings, NetworkSettingsPath);
            return settings;
        }

        private static GameObject EnsurePlayerPrefab()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            root.name = "HavenPlayer";
            root.transform.position = new Vector3(0f, 1f, 0f);
            root.AddComponent<NetworkObject>();
            var networkTransform = root.AddComponent<NetworkTransform>();
            root.AddComponent<FishNetPlayerAvatar>();

            var transformSettings = new SerializedObject(networkTransform);
            transformSettings.FindProperty("_clientAuthoritative").boolValue = false;
            transformSettings.FindProperty("_sendToOwner").boolValue = true;
            transformSettings.FindProperty("_synchronizeRotation").boolValue = false;
            transformSettings.FindProperty("_synchronizeScale").boolValue = false;
            transformSettings.ApplyModifiedPropertiesWithoutUndo();

            var renderer = root.GetComponent<Renderer>();
            if (renderer)
                renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (!prefab)
                throw new InvalidOperationException("Could not create the HavenPlayer prefab.");
            return prefab;
        }

        private static void AddPlayerToDefaultPrefabs(GameObject player)
        {
            var prefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabsPath);
            if (!prefabs)
            {
                prefabs = ScriptableObject.CreateInstance<DefaultPrefabObjects>();
                AssetDatabase.CreateAsset(prefabs, DefaultPrefabsPath);
            }

            var networkObject = player.GetComponent<NetworkObject>();
            prefabs.AddObject(networkObject, true, false);
            EditorUtility.SetDirty(prefabs);
        }

        private static void CreateDemoScene(HavenNetworkSettings settings, GameObject player)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var managerObject = new GameObject("NetworkManager");
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.SpawnablePrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabsPath);
            var transport = managerObject.AddComponent<Tugboat>();
            transport.SetPort(settings.Port);
            transport.SetMaximumClients(settings.MaximumPlayers);
            var spawner = managerObject.AddComponent<PlayerSpawner>();
            spawner.SetPlayerPrefab(player.GetComponent<NetworkObject>());
            managerObject.AddComponent<FishNetRuntimeInstaller>().Configure(manager, settings);

            spawner.Spawns = new Transform[4];
            for (var index = 0; index < spawner.Spawns.Length; index++)
            {
                var angle = index * Mathf.PI * 0.5f;
                var spawn = new GameObject($"Spawn {index + 1}").transform;
                spawn.SetParent(managerObject.transform);
                spawn.position = new Vector3(Mathf.Cos(angle) * 2f, 1f, Mathf.Sin(angle) * 2f);
                spawner.Spawns[index] = spawn;
            }

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.transform.position = new Vector3(0f, 11f, -10f);
            camera.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Server-authoritative Test Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);

            new GameObject("Demo HUD").AddComponent<HavenDemoHud>();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Could not save {ScenePath}.");
        }

        private static void RegisterScene()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(item => item.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
