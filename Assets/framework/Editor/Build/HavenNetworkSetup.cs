using System;
using System.IO;
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
        private const string AutoRefreshSessionKey = "Haven.Network.SurvivalPrefabRefresh.v4";
        public const string ScenePath = "Assets/Scenes/FrameworkDemo.unity";
        public const string WorldScenePath = "Assets/Scenes/WorldGenMap.unity";
        public const string PlayerPrefabPath = "Assets/Prefabs/Network/HavenPlayer.prefab";
        public const string SurvivalPlayerPrefabPath = "Assets/Art/all/SurvivalEngine/Prefabs/PlayerCharacter.prefab";
        public const string NetworkSettingsPath = "Assets/Resources/HavenNetworkSettings.asset";
        private const string DefaultPrefabsPath = "Assets/DefaultPrefabObjects.asset";

        [InitializeOnLoadMethod]
        private static void RefreshStaleDemoAfterCompile()
        {
            if (SessionState.GetBool(AutoRefreshSessionKey, false))
                return;
            SessionState.SetBool(AutoRefreshSessionKey, true);
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                var simulationType = Type.GetType("Haven.Gameplay.NetworkSurvivalPlayerAdapter, Assembly-CSharp");
                if (simulationType == null)
                    return;
                var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                var stalePlayer = !player || player.GetComponent(simulationType) == null;
                var staleScene = File.Exists(ScenePath) && File.ReadAllText(ScenePath).Contains("Server-authoritative Test Ground");
                if (stalePlayer || staleScene)
                    CreateOrRefreshDemo();
            };
        }

        [MenuItem("Haven/Network/1. Create or Refresh Demo")]
        public static void CreateOrRefreshDemo()
        {
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/Network");
            EnsureFolder("Assets/Resources");

            var settings = EnsureNetworkSettings();
            EditorUtility.SetDirty(settings);
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
            var worldScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(WorldScenePath);
            var prefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabsPath);

            if (!settings || !player || !scene || !worldScene || !prefabs)
                throw new InvalidOperationException("Haven network demo is incomplete. Run Haven/Network/1. Create or Refresh Demo.");
            if (!player.TryGetComponent<NetworkObject>(out var networkObject) ||
                !player.TryGetComponent<FishNetPlayerAvatar>(out _) ||
                !player.TryGetComponent<NetworkTransform>(out _))
                throw new InvalidOperationException("HavenPlayer prefab is missing required FishNet components.");
            var simulationType = Type.GetType("Haven.Gameplay.NetworkSurvivalPlayerAdapter, Assembly-CSharp");
            if (simulationType == null || player.GetComponent(simulationType) == null)
                throw new InvalidOperationException("HavenPlayer prefab is missing the SurvivalEngine network adapter.");
            foreach (var renderer in player.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (!material || !material.shader || !material.shader.isSupported || material.shader.name == "Hidden/InternalErrorShader")
                        throw new InvalidOperationException($"HavenPlayer renderer '{renderer.name}' has a missing or unsupported material shader.");
                }
            }
            if (!prefabs.Prefabs.Contains(networkObject))
                throw new InvalidOperationException("HavenPlayer is not registered in FishNet DefaultPrefabObjects.");
            if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == ScenePath))
                throw new InvalidOperationException("FrameworkDemo scene is not enabled in build settings.");
            if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == WorldScenePath))
                throw new InvalidOperationException("WorldGenMap scene is not enabled in build settings.");
            if (settings.ProtocolVersion != 4)
                throw new InvalidOperationException("Room protocol version must be 4.");
            if (settings.MaximumConnections <= settings.MaximumPlayers)
                throw new InvalidOperationException("Transport capacity must exceed room capacity so full-room errors can be returned.");

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
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SurvivalPlayerPrefabPath);
            if (!source)
                throw new InvalidOperationException($"Survival player prefab is missing: {SurvivalPlayerPrefabPath}");

            var root = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (!root)
                throw new InvalidOperationException("Could not instantiate the SurvivalEngine player prefab.");
            root.name = "HavenPlayer";
            root.transform.position = new Vector3(0f, 1f, 0f);
            var gameplayBehaviours = root.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(behaviour => behaviour && behaviour.enabled).ToArray();
            foreach (var behaviour in gameplayBehaviours)
                behaviour.enabled = false;

            root.AddComponent<NetworkObject>();
            var networkTransform = root.AddComponent<NetworkTransform>();
            root.AddComponent<FishNetPlayerAvatar>();

            var simulationType = Type.GetType("Haven.Gameplay.NetworkSurvivalPlayerAdapter, Assembly-CSharp");
            if (simulationType == null || !typeof(MonoBehaviour).IsAssignableFrom(simulationType))
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw new InvalidOperationException("NetworkSurvivalPlayerAdapter has not compiled into Assembly-CSharp.");
            }
            var simulation = root.AddComponent(simulationType) as MonoBehaviour;
            simulationType.GetMethod("ConfigureGameplayBehaviours")?.Invoke(simulation, new object[] { gameplayBehaviours });
            simulation.enabled = true;

            var body = root.GetComponent<Rigidbody>();
            if (body)
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            var transformSettings = new SerializedObject(networkTransform);
            transformSettings.FindProperty("_clientAuthoritative").boolValue = false;
            transformSettings.FindProperty("_sendToOwner").boolValue = true;
            transformSettings.FindProperty("_synchronizeRotation").boolValue = false;
            transformSettings.FindProperty("_synchronizeScale").boolValue = false;
            transformSettings.ApplyModifiedPropertiesWithoutUndo();

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
            transport.SetMaximumClients(settings.MaximumConnections);
            var authenticator = managerObject.AddComponent<HavenRoomAuthenticator>();
            authenticator.Configure(settings);
            var spawner = managerObject.AddComponent<PlayerSpawner>();
            spawner.SetPlayerPrefab(player.GetComponent<NetworkObject>());
            managerObject.AddComponent<FishNetRuntimeInstaller>().Configure(manager, settings, player.GetComponent<NetworkObject>());

            spawner.Spawns = new Transform[4];
            for (var index = 0; index < spawner.Spawns.Length; index++)
            {
                var angle = index * Mathf.PI * 0.5f;
                var spawn = new GameObject($"Spawn {index + 1}").transform;
                spawn.SetParent(managerObject.transform);
                spawn.position = new Vector3(Mathf.Cos(angle) * 2f, 1f, Mathf.Sin(angle) * 2f);
                spawner.Spawns[index] = spawn;
            }

            var presentation = new GameObject("LobbyPresentation");

            var cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(presentation.transform);
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.transform.position = new Vector3(0f, 11f, -10f);
            camera.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

            var lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(presentation.transform);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var hud = new GameObject("Demo HUD");
            hud.transform.SetParent(presentation.transform);
            hud.AddComponent<HavenDemoHud>();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Could not save {ScenePath}.");
        }

        private static void RegisterScene()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(item => item.path == ScenePath || item.path == WorldScenePath);
            var mainMenuIndex = scenes.FindIndex(item => item.path == "Assets/Scenes/MainMenu.unity");
            var loadingIndex = scenes.FindIndex(item => item.path == "Assets/Scenes/Loading.unity");
            var insertionIndex = loadingIndex >= 0
                ? loadingIndex + 1
                : mainMenuIndex >= 0
                    ? mainMenuIndex + 1
                    : 0;
            scenes.Insert(insertionIndex, new EditorBuildSettingsScene(WorldScenePath, true));
            scenes.Insert(insertionIndex + 1, new EditorBuildSettingsScene(ScenePath, true));
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
