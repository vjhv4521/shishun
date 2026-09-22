using System;
using System.Collections;
using Haven.Framework.Bootstrap;
using Haven.Framework.Core;
using Haven.Framework.Services;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Haven.Networking
{
    // Opt-in player-process probe. Example: -haven-smoke-role=host or
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
                !bootstrap.Context.Services.TryResolve<IRoomService>(out var rooms) ||
                !bootstrap.Context.Services.TryResolve<ICoopGameplayService>(out var gameplay))
            {
                Fail("network, room, or gameplay service missing");
                yield break;
            }

            var settings = Resources.Load<HavenNetworkSettings>(HavenNetworkSettings.DefaultResourceName);
            var port = settings ? settings.Port : (ushort)7770;
            FrameworkResult connected = default;
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
            yield return WaitFor(() => gameplay.Current.HasState && gameplay.Current.Players.Length >= 2, 30f);
            if (!gameplay.Current.HasState || gameplay.Current.Players.Length < 2)
            {
                Fail("two-player gameplay snapshot missing");
                yield break;
            }
            Debug.Log($"HAVEN_SMOKE_INGAME role={_role} revision={gameplay.Current.Revision}");

            if (_role == "host")
            {
                FrameworkResult<GameplaySnapshot> collected = default;
                yield return gameplay.Collect("wood_01", result => collected = result);
                if (!collected.Succeeded)
                {
                    Fail($"authoritative collect: {collected.Error}");
                    yield break;
                }
                if (!collected.Value.TryGetLocalPlayer(out var player) || ItemCount(player.Inventory, GameplayItemIds.Wood) != 1)
                {
                    Fail("private inventory did not receive exactly one wood");
                    yield break;
                }
                Debug.Log("HAVEN_SMOKE_COLLECTED role=host node=wood_01 wood=1");
            }
            else
            {
                yield return WaitFor(() => NodeRemaining(gameplay.Current, "wood_01") == 7, 30f);
                if (NodeRemaining(gameplay.Current, "wood_01") != 7)
                {
                    Fail("guest did not receive the host's resource update");
                    yield break;
                }
                if (!gameplay.Current.TryGetLocalPlayer(out var player) || ItemCount(player.Inventory, GameplayItemIds.Wood) != 0)
                {
                    Fail("host's private wood leaked into guest inventory");
                    yield break;
                }
                Debug.Log("HAVEN_SMOKE_SYNCED role=guest node=wood_01 remaining=7 privateWood=0");
            }

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

        private static int ItemCount(GameplayInventoryItemSnapshot[] items, string itemId)
        {
            foreach (var item in items ?? Array.Empty<GameplayInventoryItemSnapshot>())
                if (item.ItemId == itemId)
                    return item.Quantity;
            return 0;
        }

        private static int NodeRemaining(GameplaySnapshot snapshot, string nodeId)
        {
            foreach (var node in snapshot.ResourceNodes ?? Array.Empty<GameplayResourceNodeSnapshot>())
                if (node.NodeId == nodeId)
                    return node.Remaining;
            return -1;
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
