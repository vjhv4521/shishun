using System.Collections;
using System.Collections.Generic;
using FishNet.Managing;
using Haven.Framework.Bootstrap;
using Haven.Framework.Services;
using Haven.Networking;
using SurvivalEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Haven.Gameplay
{
    /// <summary>Bridges the baked Survival Engine scene to the network avatar without changing standalone play.</summary>
    public sealed class WorldGenMultiplayerAdapter : MonoBehaviour
    {
        private const string WorldSceneName = "WorldGenMap";
        private static WorldGenMultiplayerAdapter _instance;
        private readonly List<Behaviour> _disabledLobbyComponents = new List<Behaviour>();
        private readonly List<GameObject> _disabledLobbyObjects = new List<GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureCreated()
        {
            if (_instance)
                return;
            var root = new GameObject("[HavenWorldGenMultiplayerAdapter]");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<WorldGenMultiplayerAdapter>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            var existing = SceneManager.GetSceneByName(WorldSceneName);
            if (existing.IsValid() && existing.isLoaded)
                StartCoroutine(AdaptWhenReady(existing));
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            RestoreLobbyComponents();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == WorldSceneName)
                StartCoroutine(AdaptWhenReady(scene));
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (scene.name == WorldSceneName)
            {
                if (PlayerData.IsTransientSession())
                    PlayerData.EndTransientSession();
                RestoreLobbyComponents();
            }
        }

        private IEnumerator AdaptWhenReady(Scene worldScene)
        {
            var deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline && !IsNetworkRoomActive())
                yield return null;
            if (!IsNetworkRoomActive())
                yield break;

            DisableLobbyPresentation();

            foreach (var player in FindObjectsByType<PlayerCharacter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (player.gameObject.scene == worldScene && !player.GetComponent<NetworkSurvivalPlayerAdapter>())
                    player.gameObject.SetActive(false);
            }

#if !UNITY_SERVER && !HAVEN_SERVER_BUILD
            FishNetPlayerAvatar localAvatar = null;
            while (Time.realtimeSinceStartup < deadline && !localAvatar)
            {
                foreach (var avatar in FindObjectsByType<FishNetPlayerAvatar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (avatar.IsOwner)
                    {
                        localAvatar = avatar;
                        break;
                    }
                }
                if (!localAvatar)
                    yield return null;
            }

            var worldCamera = TheCamera.Get();
            if (worldCamera && localAvatar)
                worldCamera.follow_target = localAvatar.gameObject;
#endif
        }

        private static bool IsNetworkRoomActive()
        {
            var context = GameBootstrap.Instance?.Context;
            if (context != null && context.Services.TryResolve<IRoomService>(out var room) &&
                (room.Current.Phase == RoomPhase.Loading || room.Current.Phase == RoomPhase.InGame))
                return true;

            var manager = FindAnyObjectByType<NetworkManager>();
            return manager && manager.ServerManager.Started;
        }

        private void DisableLobbyPresentation()
        {
            RestoreLobbyComponents();
            var lobby = SceneManager.GetSceneByName("FrameworkDemo");
            if (lobby.IsValid() && lobby.isLoaded)
            {
                foreach (var root in lobby.GetRootGameObjects())
                {
                    if (root.name == "LobbyPresentation" && root.activeSelf)
                    {
                        root.SetActive(false);
                        _disabledLobbyObjects.Add(root);
                        return;
                    }
                }
            }

            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (camera.gameObject.scene.name == "FrameworkDemo" && camera.enabled)
                {
                    camera.enabled = false;
                    _disabledLobbyComponents.Add(camera);
                }
            }
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (listener.gameObject.scene.name == "FrameworkDemo" && listener.enabled)
                {
                    listener.enabled = false;
                    _disabledLobbyComponents.Add(listener);
                }
            }
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (light.gameObject.scene.name == "FrameworkDemo" && light.enabled)
                {
                    light.enabled = false;
                    _disabledLobbyComponents.Add(light);
                }
            }
        }

        private void RestoreLobbyComponents()
        {
            foreach (var component in _disabledLobbyComponents)
            {
                if (component)
                    component.enabled = true;
            }
            _disabledLobbyComponents.Clear();
            foreach (var gameObject in _disabledLobbyObjects)
            {
                if (gameObject)
                    gameObject.SetActive(true);
            }
            _disabledLobbyObjects.Clear();
        }
    }
}
