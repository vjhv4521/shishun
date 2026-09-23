using System;
using System.Collections;
using Haven.Framework.Bootstrap;
using Haven.Framework.Core;
using Haven.Framework.Services;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Haven.Networking
{
    // Opt-in two-player process probe. Example: -haven-smoke-role=host or
    // -haven-smoke-role=guest -haven-smoke-room=ABC123.
    internal sealed class HavenCoopSmokeRunner : MonoBehaviour
    {
        private const string LobbyScene = "FrameworkDemo";
        private string _role;
        private string _roomCode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfRequested()
        {
            var role = ReadArgument("-haven-smoke-role=");
            if (role != "host" && role != "guest")
                return;
            var gameObject = new GameObject("[HavenCoopSmokeRunner]");
            DontDestroyOnLoad(gameObject);
            var runner = gameObject.AddComponent<HavenCoopSmokeRunner>();
            runner._role = role;
            runner._roomCode = ReadArgument("-haven-smoke-room=");
        }

        private void Start()
        {
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            Debug.Log($"HAVEN_SMOKE_START role={_role}");
            if (_role == "guest" && string.IsNullOrWhiteSpace(_roomCode))
            {
                Fail("guest requires -haven-smoke-room");
                yield break;
            }

            if (SceneManager.GetActiveScene().name != LobbyScene)
            {
                var loading = SceneManager.LoadSceneAsync(LobbyScene, LoadSceneMode.Single);
                if (loading == null)
                {
                    Fail("could not load FrameworkDemo");
                    yield break;
                }
                while (!loading.isDone)
                    yield return null;
            }

            yield return null;
            var bootstrap = GameBootstrap.Instance;
            if (!bootstrap)
            {
                Fail("bootstrap missing");
                yield break;
            }
            bootstrap.RestartForCurrentScene();
            yield return WaitFor(() => bootstrap.State == BootstrapState.Running || bootstrap.State == BootstrapState.Failed, 90f);
            if (bootstrap.State != BootstrapState.Running || bootstrap.Context == null)
            {
                Fail($"bootstrap failed: {bootstrap.LastError}");
                yield break;
            }

            if (!bootstrap.Context.Services.TryResolve<INetworkService>(out var network) ||
                !bootstrap.Context.Services.TryResolve<INetworkHostService>(out var host) ||
                !bootstrap.Context.Services.TryResolve<IRoomService>(out var rooms) ||
                !bootstrap.Context.Services.TryResolve<ISurvivalSessionService>(out var survival))
            {
                Fail("network, room, host, or survival session service missing");
                yield break;
            }

            var settings = Resources.Load<HavenNetworkSettings>(HavenNetworkSettings.DefaultResourceName);
            var port = settings ? settings.Port : (ushort)7770;
            FrameworkResult connected = default;
            if (_role == "host")
                yield return host.StartHost(new NetworkEndpoint("127.0.0.1", port), result => connected = result);
            else
                yield return network.Connect(new NetworkEndpoint("127.0.0.1", port), result => connected = result);
            if (!connected.Succeeded)
            {
                Fail($"connect: {connected.Error}");
                yield break;
            }
            Debug.Log($"HAVEN_SMOKE_CONNECTED role={_role}");

            FrameworkResult<RoomSnapshot> roomResult = default;
            if (_role == "host")
            {
                yield return rooms.CreateRoom("SmokeHost", result => roomResult = result);
                if (!roomResult.Succeeded)
                {
                    Fail($"create: {roomResult.Error}");
                    yield break;
                }
                Debug.Log($"HAVEN_SMOKE_ROOM={roomResult.Value.RoomCode}");
                yield return WaitFor(() => rooms.Current.MemberCount >= 2 && GuestReady(rooms.Current), 90f);
                if (rooms.Current.MemberCount < 2 || !GuestReady(rooms.Current))
                {
                    Fail("guest did not join and ready");
                    yield break;
                }
                yield return rooms.StartGame(result => roomResult = result);
                if (!roomResult.Succeeded)
                {
                    Fail($"start game: {roomResult.Error}");
                    yield break;
                }
            }
            else
            {
                yield return rooms.JoinRoom(_roomCode, "SmokeGuest", result => roomResult = result);
                if (!roomResult.Succeeded)
                {
                    Fail($"join: {roomResult.Error}");
                    yield break;
                }
                yield return rooms.SetReady(true, result => roomResult = result);
                if (!roomResult.Succeeded)
                {
                    Fail($"ready: {roomResult.Error}");
                    yield break;
                }
            }

            yield return WaitFor(() => rooms.Current.Phase == RoomPhase.InGame, 90f);
            if (rooms.Current.Phase != RoomPhase.InGame)
            {
                Fail($"game scene not ready: {rooms.Current.Phase}");
                yield break;
            }
            yield return WaitFor(() => survival.Current.HasState &&
                survival.Current.Players != null && survival.Current.Players.Length >= 2, 30f);
            if (!survival.Current.HasState || survival.Current.Players == null ||
                survival.Current.Players.Length < 2)
            {
                Fail("two-player survival snapshot missing");
                yield break;
            }
            var hasLocalPlayer = false;
            foreach (var player in survival.Current.Players)
            {
                if (player.PlayerId == survival.Current.LocalPlayerId)
                    hasLocalPlayer = true;
            }
            if (!hasLocalPlayer)
            {
                Fail("local player missing from survival snapshot");
                yield break;
            }
            var probe = SurvivalCommand.Create(SurvivalCommandType.Interact);
            probe.TargetUid = "haven-smoke-missing-target";
            var hasResult = false;
            SurvivalCommandCompleted commandResult = default;
            using (bootstrap.Context.Events.Subscribe<SurvivalCommandCompleted>(result =>
                   {
                       if (result.RequestId != probe.RequestId)
                           return;
                       commandResult = result;
                       hasResult = true;
                   }))
            {
                if (!survival.Submit(probe))
                {
                    Fail("could not submit authoritative command probe");
                    yield break;
                }
                yield return WaitFor(() => hasResult, 10f);
                if (!hasResult || commandResult.Succeeded || commandResult.ErrorCode != GameplayErrorCodes.NotFound)
                {
                    Fail("server did not reject the missing interaction target");
                    yield break;
                }
                hasResult = false;
                if (!survival.Submit(probe))
                {
                    Fail("could not resubmit duplicate command probe");
                    yield break;
                }
                yield return WaitFor(() => hasResult, 10f);
                if (!hasResult || commandResult.Succeeded || commandResult.ErrorCode != GameplayErrorCodes.DuplicateRequest)
                {
                    Fail("server did not reject the duplicate request ID");
                    yield break;
                }
            }
            var activeCameras = 0;
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (camera.enabled) activeCameras++;
            var activeListeners = 0;
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (listener.enabled) activeListeners++;
            if (activeCameras != 1 || activeListeners != 1)
            {
                Fail($"expected one camera and listener, got {activeCameras} cameras and {activeListeners} listeners");
                yield break;
            }
            Debug.Log($"HAVEN_SMOKE_INGAME role={_role} revision={survival.Current.Revision} players={survival.Current.Players.Length}");

            Debug.Log($"HAVEN_SMOKE_PASS role={_role}");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(0);
        }

        private static IEnumerator WaitFor(Func<bool> ready, float timeoutSeconds)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!ready() && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        private static bool GuestReady(RoomSnapshot snapshot)
        {
            foreach (var member in snapshot.Members ?? Array.Empty<RoomMemberSnapshot>())
                if (!member.IsHost && member.IsReady)
                    return true;
            return false;
        }

        private void Fail(string reason)
        {
            Debug.LogError($"HAVEN_SMOKE_FAIL role={_role} reason={reason}");
            StartCoroutine(QuitFailed());
        }

        private static IEnumerator QuitFailed()
        {
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(1);
        }

        private static string ReadArgument(string prefix)
        {
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return argument.Substring(prefix.Length).Trim();
            return string.Empty;
        }
    }
}
