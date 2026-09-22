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
    internal sealed class FishNetNetworkService : INetworkService, INetworkHostService, ILLMService, IDisposable
    {
        private readonly FrameworkContext _context;
        private readonly NetworkManager _manager;
        private readonly HavenNetworkSettings _settings;
        private readonly HavenRoomAuthenticator _authenticator;
        private readonly NetworkStateMachine _stateMachine = new NetworkStateMachine();
        private FishNetPlayerAvatar _localPlayer;
        private bool _disposed;
        private bool _disconnectRequested;

        public FishNetNetworkService(
            FrameworkContext context,
            NetworkManager manager,
            HavenNetworkSettings settings,
            HavenRoomAuthenticator authenticator)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _manager = manager ? manager : throw new ArgumentNullException(nameof(manager));
            _settings = settings ? settings : throw new ArgumentNullException(nameof(settings));
            _authenticator = authenticator ? authenticator : throw new ArgumentNullException(nameof(authenticator));

            _manager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            _manager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            _authenticator.OnClientAuthenticationResult += OnClientAuthenticationResult;
            if (_manager.ClientManager.Started)
                _stateMachine.Reset(NetworkState.Connecting);
        }

        public NetworkState State => _stateMachine.State;
        public bool IsConnected => State == NetworkState.Connected;
        public bool IsHosting => _manager && _manager.ServerManager.Started && _manager.ClientManager.Started && IsConnected;
        public int ConnectedPeerCount
        {
            get
            {
                if (!_manager)
                    return 0;
                if (!_manager.ServerManager.Started)
                    return IsConnected ? 1 : 0;

                var count = 0;
                foreach (var connection in _manager.ServerManager.Clients.Values)
                {
                    if (connection.IsAuthenticated)
                        count++;
                }
                return count;
            }
        }

        public NetworkEndpoint ConnectedEndpoint { get; private set; }
        public NetworkEndpoint ShareEndpoint { get; private set; }
        public int MaximumPlayers => _settings.MaximumPlayers;
        public FrameworkError LastError { get; private set; }

        public IEnumerator StartHost(NetworkEndpoint endpoint, Action<FrameworkResult> completed)
        {
            if (!TryBegin(false, out var beginFailure))
            {
                completed?.Invoke(beginFailure);
                yield break;
            }

            LastError = null;
            _disconnectRequested = false;
            ConnectedEndpoint = new NetworkEndpoint("127.0.0.1", endpoint.Port);
            ShareEndpoint = new NetworkEndpoint(LanAddressResolver.ResolveIPv4(), endpoint.Port);
            SetState(NetworkState.Starting, $"Starting a LAN room on UDP port {endpoint.Port}.");
            ConfigureTransport(ConnectedEndpoint);

            if (!_manager.ServerManager.Started && !_manager.ServerManager.StartConnection())
            {
                completed?.Invoke(Fail("NETWORK_HOST_SERVER_REJECTED", "FishNet could not start the room server.", true));
                yield break;
            }

            SetState(NetworkState.Connecting, "Room server started; authenticating the local host client.");
            if (!_manager.ClientManager.Started && !_manager.ClientManager.StartConnection())
            {
                completed?.Invoke(Fail("NETWORK_HOST_CLIENT_REJECTED", "The room server started, but its local client could not connect.", true));
                StopConnections();
                yield break;
            }

            yield return WaitForConnection(completed, "NETWORK_HOST_TIMEOUT", $"Could not create the room within {_settings.ConnectTimeoutSeconds:0.#} seconds.");
        }

        public IEnumerator Connect(NetworkEndpoint endpoint, Action<FrameworkResult> completed)
        {
            if (!TryBegin(true, out var beginFailure))
            {
                completed?.Invoke(beginFailure);
                yield break;
            }

            LastError = null;
            _disconnectRequested = false;
            ConnectedEndpoint = endpoint;
            ShareEndpoint = default;
            SetState(NetworkState.Starting, $"Starting a client connection to {endpoint}.");
            ConfigureTransport(endpoint);

            if (!_manager.ClientManager.StartConnection())
            {
                completed?.Invoke(Fail("NETWORK_START_REJECTED", "FishNet could not start the client connection.", true));
                yield break;
            }

            if (State == NetworkState.Starting)
                SetState(NetworkState.Connecting, $"Transport started; authenticating with {endpoint}.");
            yield return WaitForConnection(completed, "NETWORK_CONNECT_TIMEOUT", $"Could not connect to {endpoint} within {_settings.ConnectTimeoutSeconds:0.#} seconds.");
        }

        public void Disconnect()
        {
            if (_disposed)
                return;
            if (_manager.ServerManager.Started)
            {
                StopHost();
                return;
            }

            _disconnectRequested = true;
            if (_manager.ClientManager.Started)
                _manager.ClientManager.StopConnection();
            else if (State != NetworkState.Disconnected)
                SetState(NetworkState.Disconnected, "Client disconnect completed.");
            ConnectedEndpoint = default;
        }

        public void StopHost()
        {
            if (_disposed || !_manager)
                return;
            _disconnectRequested = true;
            StopConnections();
            ConnectedEndpoint = default;
            ShareEndpoint = default;
            if (State != NetworkState.Disconnected)
                SetState(NetworkState.Disconnected, "Room host stopped.");
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
                _authenticator.OnClientAuthenticationResult -= OnClientAuthenticationResult;
                StopConnections();
            }
            _localPlayer = null;
            ConnectedEndpoint = default;
            ShareEndpoint = default;
            ForceState(NetworkState.Disconnected, "Network service disposed.");
        }

        private IEnumerator WaitForConnection(Action<FrameworkResult> completed, string timeoutCode, string timeoutMessage)
        {
            var deadline = Time.realtimeSinceStartup + _settings.ConnectTimeoutSeconds;
            while ((State == NetworkState.Starting || State == NetworkState.Connecting) && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (State == NetworkState.Connected)
            {
                completed?.Invoke(FrameworkResult.Success());
                yield break;
            }
            if (State == NetworkState.Failed && LastError != null)
            {
                completed?.Invoke(FrameworkResult.Failure(LastError));
                yield break;
            }

            var timeout = Fail(timeoutCode, timeoutMessage, true);
            StopConnections();
            completed?.Invoke(timeout);
        }

        private bool TryBegin(bool clientOnly, out FrameworkResult failure)
        {
            if (_disposed)
            {
                failure = Failure("NETWORK_DISPOSED", "Network service has already been disposed.", false);
                return false;
            }
            if (State == NetworkState.Starting || State == NetworkState.Connecting)
            {
                failure = Failure("NETWORK_OPERATION_IN_PROGRESS", "A room connection operation is already in progress.", true);
                return false;
            }
            if (_manager.ServerManager.Started || IsHosting)
            {
                failure = Failure("NETWORK_ALREADY_HOSTING", "This process is already hosting a room.", false);
                return false;
            }
            if (_manager.ClientManager.Started || IsConnected)
            {
                failure = Failure("NETWORK_ALREADY_CONNECTED", clientOnly ? "This client is already connected to a room." : "Disconnect from the current room before creating another one.", false);
                return false;
            }

            failure = default;
            return true;
        }

        private void ConfigureTransport(NetworkEndpoint endpoint)
        {
            _manager.TransportManager.Transport.SetClientAddress(endpoint.Host);
            _manager.TransportManager.Transport.SetPort(endpoint.Port);
            _manager.TransportManager.Transport.SetMaximumClients(_settings.MaximumPlayers + 1);
        }

        private void StopConnections()
        {
            if (!_manager)
                return;
            if (_manager.ClientManager.Started)
                _manager.ClientManager.StopConnection();
            if (_manager.ServerManager.Started)
                _manager.ServerManager.StopConnection(true);
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Starting:
                    if (State == NetworkState.Idle || State == NetworkState.Disconnected || State == NetworkState.Failed)
                        SetState(NetworkState.Starting, "FishNet client is starting.");
                    break;
                case LocalConnectionState.Started:
                    if (State == NetworkState.Starting)
                        SetState(NetworkState.Connecting, "Transport connected; waiting for room authentication.");
                    break;
                case LocalConnectionState.Stopping:
                    break;
                case LocalConnectionState.Stopped:
                    _localPlayer = null;
                    if (State != NetworkState.Failed && State != NetworkState.Disconnected)
                        SetState(NetworkState.Disconnected, _disconnectRequested ? "Client disconnected." : "The room connection closed.");
                    if (_manager.ServerManager.Started && !_disconnectRequested)
                        _manager.ServerManager.StopConnection(true);
                    break;
            }
        }

        private void OnClientAuthenticationResult(bool accepted, string errorCode, string errorMessage)
        {
            if (accepted)
            {
                LastError = null;
                SetState(NetworkState.Connected, "Room authentication succeeded.");
                return;
            }

            var code = string.IsNullOrWhiteSpace(errorCode) ? "NETWORK_CONNECTION_REJECTED" : errorCode;
            var message = string.IsNullOrWhiteSpace(errorMessage) ? "The room rejected this connection." : errorMessage;
            LastError = new FrameworkError(code, message, "FishNet", code != "NETWORK_PROTOCOL_MISMATCH");
            SetState(NetworkState.Failed, message);
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            _context.Events.Publish(new NetworkPeerCountChanged(ConnectedPeerCount));
        }

        private FrameworkResult Fail(string code, string message, bool retryable)
        {
            LastError = new FrameworkError(code, message, "FishNet", retryable);
            SetState(NetworkState.Failed, message);
            return FrameworkResult.Failure(LastError);
        }

        private void SetState(NetworkState value, string reason)
        {
            if (State == value)
                return;
            var previous = State;
            if (!_stateMachine.TryTransition(value))
            {
                GameLog.Warning("FishNet", $"Rejected invalid network state transition {previous} -> {value}. {reason}", "NETWORK_STATE_INVALID");
                return;
            }
            PublishState(previous, value, reason);
        }

        private void ForceState(NetworkState value, string reason)
        {
            if (State == value)
                return;
            var previous = State;
            _stateMachine.Reset(value);
            PublishState(previous, value, reason);
        }

        private void PublishState(NetworkState previous, NetworkState value, string reason)
        {
            _context.Events.Publish(new NetworkStateChanged(previous, value, reason));
            _context.Events.Publish(new NetworkPeerCountChanged(ConnectedPeerCount));
            GameLog.Info("FishNet", $"Network state {previous} -> {value}. {reason}", "NETWORK_STATE", _context.CorrelationId);
        }

        private static FrameworkResult Failure(string code, string message, bool retryable)
        {
            return FrameworkResult.Failure(new FrameworkError(code, message, "FishNet", retryable));
        }
    }
}
