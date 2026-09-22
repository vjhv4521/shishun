using System;
using FishNet.Authenticating;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace Haven.Networking
{
    [DisallowMultipleComponent]
    public sealed class HavenRoomAuthenticator : Authenticator
    {
        [SerializeField] private HavenNetworkSettings settings;

        public override event Action<NetworkConnection, bool> OnAuthenticationResult;
        public event Action<bool, string, string> OnClientAuthenticationResult;

        public string LastClientErrorCode { get; private set; } = string.Empty;
        public string LastClientErrorMessage { get; private set; } = string.Empty;

        public override void InitializeOnce(NetworkManager networkManager)
        {
            base.InitializeOnce(networkManager);
            if (!settings)
                settings = Resources.Load<HavenNetworkSettings>(HavenNetworkSettings.DefaultResourceName);
            if (!settings)
                settings = HavenNetworkSettings.CreateRuntimeDefault();

            NetworkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            NetworkManager.ServerManager.RegisterBroadcast<RoomJoinRequest>(OnRoomJoinRequest, false);
            NetworkManager.ClientManager.RegisterBroadcast<RoomJoinResponse>(OnRoomJoinResponse);
        }

        public void Configure(HavenNetworkSettings networkSettings)
        {
            settings = networkSettings;
        }

        private void OnDestroy()
        {
            if (!Initialized || !NetworkManager)
                return;
            NetworkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            NetworkManager.ServerManager.UnregisterBroadcast<RoomJoinRequest>(OnRoomJoinRequest);
            NetworkManager.ClientManager.UnregisterBroadcast<RoomJoinResponse>(OnRoomJoinResponse);
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started)
                return;

            LastClientErrorCode = string.Empty;
            LastClientErrorMessage = string.Empty;
            NetworkManager.ClientManager.Broadcast(new RoomJoinRequest
            {
                ProtocolVersion = settings.ProtocolVersion
            });
        }

        private void OnRoomJoinRequest(NetworkConnection connection, RoomJoinRequest request, Channel channel)
        {
            if (connection.IsAuthenticated)
            {
                connection.Disconnect(true);
                return;
            }

            var decision = RoomAdmissionRules.Evaluate(
                request.ProtocolVersion,
                settings.ProtocolVersion,
                NetworkManager.ServerManager.Clients.Count,
                settings.MaximumPlayers);

            NetworkManager.ServerManager.Broadcast(connection, new RoomJoinResponse
            {
                Accepted = decision.Accepted,
                ErrorCode = decision.ErrorCode,
                ErrorMessage = decision.ErrorMessage
            }, false);
            OnAuthenticationResult?.Invoke(connection, decision.Accepted);
        }

        private void OnRoomJoinResponse(RoomJoinResponse response, Channel channel)
        {
            LastClientErrorCode = response.ErrorCode ?? string.Empty;
            LastClientErrorMessage = response.ErrorMessage ?? string.Empty;
            OnClientAuthenticationResult?.Invoke(response.Accepted, LastClientErrorCode, LastClientErrorMessage);
        }

        private struct RoomJoinRequest : IBroadcast
        {
            public int ProtocolVersion;
        }

        private struct RoomJoinResponse : IBroadcast
        {
            public bool Accepted;
            public string ErrorCode;
            public string ErrorMessage;
        }
    }
}
