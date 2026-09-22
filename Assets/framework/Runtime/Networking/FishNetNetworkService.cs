using System;
using System.Collections;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Haven.Framework.Core;
using Haven.Framework.Services;
using UnityEngine;

namespace Haven.Networking
{
    internal sealed class FishNetNetworkService : INetworkService, ILLMService, IDisposable
    {
        private readonly FrameworkContext _context;
        private readonly NetworkManager _manager;
        private readonly HavenNetworkSettings _settings;
        private FishNetPlayerAvatar _localPlayer;
        private bool _disposed;

        public FishNetNetworkService(FrameworkContext context, NetworkManager manager, HavenNetworkSettings settings)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _manager = manager ? manager : throw new ArgumentNullException(nameof(manager));
            _settings = settings ? settings : throw new ArgumentNullException(nameof(settings));

            _manager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            _manager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            State = _manager.ClientManager.Started ? NetworkState.Connected : NetworkState.Disconnected;
        }

        public NetworkState State { get; private set; }
        public bool IsConnected => State == NetworkState.Connected;
        public int ConnectedPeerCount => _manager && _manager.ServerManager.Started ? _manager.ServerManager.Clients.Count : (IsConnected ? 1 : 0);

        public IEnumerator Connect(NetworkEndpoint endpoint, Action<FrameworkResult> completed)
        {
            if (_disposed)
            {
                completed?.Invoke(Failure("NETWORK_DISPOSED", "Network service has already been disposed."));
                yield break;
            }
            if (IsConnected)
            {
                completed?.Invoke(FrameworkResult.Success());
                yield break;
            }

            _manager.TransportManager.Transport.SetClientAddress(endpoint.Host);
            _manager.TransportManager.Transport.SetPort(endpoint.Port);
            SetState(NetworkState.Connecting, $"Connecting to {endpoint}.");
            if (!_manager.ClientManager.StartConnection())
            {
                SetState(NetworkState.Failed, "FishNet rejected the client start request.");
                completed?.Invoke(Failure("NETWORK_START_REJECTED", "FishNet could not start the client connection."));
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + _settings.ConnectTimeoutSeconds;
            while (State == NetworkState.Connecting && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (State == NetworkState.Connected)
            {
                completed?.Invoke(FrameworkResult.Success());
                yield break;
            }

            if (_manager.ClientManager.Started)
                _manager.ClientManager.StopConnection();
            SetState(NetworkState.Failed, "Connection attempt timed out or was rejected.");
            completed?.Invoke(Failure("NETWORK_CONNECT_TIMEOUT", $"Could not connect to {endpoint} within {_settings.ConnectTimeoutSeconds:0.#} seconds."));
        }

        public void Disconnect()
        {
            if (_disposed || State == NetworkState.Disconnected)
                return;
            SetState(NetworkState.Disconnecting, "Disconnect requested.");
            _manager.ClientManager.StopConnection();
        }

        public IEnumerator RequestQuest(AiQuestRequest request, Action<FrameworkResult<AiQuestResponse>> completed)
        {
            if (!IsConnected || !_localPlayer)
            {
                completed?.Invoke(FrameworkResult<AiQuestResponse>.Failure(new FrameworkError(
                    "AI_NOT_CONNECTED",
                    "Connect to the Dedicated Server before requesting an AIGC quest.",
                    "AIGCClient",
                    true)));
                yield break;
            }

            yield return _localPlayer.RequestQuest(request, completed);
        }

        internal void AttachLocalPlayer(FishNetPlayerAvatar player)
        {
            _localPlayer = player;
        }

        internal void DetachLocalPlayer(FishNetPlayerAvatar player)
        {
            if (_localPlayer == player)
                _localPlayer = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_manager)
            {
                _manager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
                _manager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
                if (_manager.ClientManager.Started)
                    _manager.ClientManager.StopConnection();
            }
            _localPlayer = null;
            SetState(NetworkState.Disconnected, "Network service disposed.");
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Starting:
                    SetState(NetworkState.Connecting, "FishNet client is starting.");
                    break;
                case LocalConnectionState.Started:
                    SetState(NetworkState.Connected, "FishNet client connected.");
                    break;
                case LocalConnectionState.Stopping:
                    SetState(NetworkState.Disconnecting, "FishNet client is stopping.");
                    break;
                case LocalConnectionState.Stopped:
                    SetState(NetworkState.Disconnected, "FishNet client stopped.");
                    break;
            }
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            _context.Events.Publish(new NetworkPeerCountChanged(ConnectedPeerCount));
        }

        private void SetState(NetworkState value, string reason)
        {
            if (State == value)
                return;
            var previous = State;
            State = value;
            _context.Events.Publish(new NetworkStateChanged(previous, value, reason));
            _context.Events.Publish(new NetworkPeerCountChanged(ConnectedPeerCount));
            GameLog.Info("FishNet", $"Network state {previous} -> {value}. {reason}", "NETWORK_STATE", _context.CorrelationId);
        }

        private static FrameworkResult Failure(string code, string message)
        {
            return FrameworkResult.Failure(new FrameworkError(code, message, "FishNet", true));
        }
    }
}
