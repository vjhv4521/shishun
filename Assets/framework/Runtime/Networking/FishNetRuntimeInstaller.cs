using System;
using System.Collections;
using FishNet.Managing;
using FishNet.Object;
using Haven.Framework.Composition;
using Haven.Framework.Core;
using Haven.Framework.Demo;
using Haven.Framework.Services;
using UnityEngine;

namespace Haven.Networking
{
    [DisallowMultipleComponent]
    public sealed class FishNetRuntimeInstaller : MonoBehaviour, IFrameworkServiceInstaller
    {
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private HavenNetworkSettings settings;
        [SerializeField] private NetworkObject playerPrefab;

        private FrameworkContext _context;
        private FishNetNetworkService _service;
        private FishNetRoomService _roomService;
        private ICoopGameplayService _gameplayService;

        public int Order => 100;
        public HavenNetworkSettings Settings => settings;

        public IEnumerator Install(FrameworkContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            if (!networkManager)
                networkManager = FindAnyObjectByType<NetworkManager>();
            if (!networkManager)
                throw new InvalidOperationException("FishNetRuntimeInstaller requires a FishNet NetworkManager in the startup scene.");

            if (!settings)
                settings = Resources.Load<HavenNetworkSettings>(HavenNetworkSettings.DefaultResourceName);
            if (!settings)
            {
                settings = HavenNetworkSettings.CreateRuntimeDefault();
                GameLog.Warning("FishNet", "HavenNetworkSettings asset was not found; using local development defaults.", "NETWORK_DEFAULT_SETTINGS");
            }

            if (settings.TransportProtocol != HavenTransportProtocol.TugboatUdp)
                throw new InvalidOperationException($"Unsupported Haven transport protocol: {settings.TransportProtocol}.");

            networkManager.TransportManager.Transport.SetPort(settings.Port);
            networkManager.TransportManager.Transport.SetMaximumClients(settings.MaximumConnections);
            var authenticator = networkManager.ServerManager.GetAuthenticator() as HavenRoomAuthenticator;
            if (!authenticator)
            {
                authenticator = networkManager.GetComponent<HavenRoomAuthenticator>();
                if (!authenticator)
                    authenticator = networkManager.gameObject.AddComponent<HavenRoomAuthenticator>();
                authenticator.Configure(settings);
                networkManager.ServerManager.SetAuthenticator(authenticator);
            }
            else
            {
                authenticator.Configure(settings);
            }

            FindAnyObjectByType<HavenDemoHud>()?.SetDefaultEndpoint(settings.DefaultHost, settings.Port);
            _service = new FishNetNetworkService(context, networkManager, settings, authenticator);
            _roomService = new FishNetRoomService(context, networkManager, settings, this, playerPrefab);
            _gameplayService = new SurvivalCoopGameplayFacade(_service);
            context.Services.Register<INetworkService>(_service);
            context.Services.Register<INetworkHostService>(_service);
            context.Services.Register<ILLMService>(_service);
            context.Services.Register<IRoomService>(_roomService);
            context.Services.Register<ICoopGameplayService>(_gameplayService);
            context.Services.Register<ISurvivalSessionService>(_service);

#if (UNITY_SERVER || HAVEN_SERVER_BUILD) && !UNITY_EDITOR
            // Dedicated players have no local client or menu to start the transport.
            // Install all room/gameplay handlers before accepting connections.
            if (!networkManager.ServerManager.StartConnection())
                throw new InvalidOperationException($"Dedicated Server could not listen on UDP port {settings.Port}.");
            GameLog.Info("FishNet", $"Dedicated Server listening on UDP port {settings.Port}.");
#endif

            yield break;
        }

        public void Uninstall()
        {
            if (_context != null)
            {
                _context.Services.Remove<ISurvivalSessionService>(false);
                _context.Services.Remove<ICoopGameplayService>(false);
                _context.Services.Remove<IRoomService>(false);
                _context.Services.Remove<ILLMService>(false);
                _context.Services.Remove<INetworkHostService>(false);
                _context.Services.Remove<INetworkService>(false);
            }
            if (_gameplayService is IDisposable disposableGameplay)
                disposableGameplay.Dispose();
            _roomService?.Dispose();
            _service?.Dispose();
            _gameplayService = null;
            _roomService = null;
            _service = null;
            _context = null;
        }

        private void Update()
        {
        }

        public void Configure(NetworkManager manager, HavenNetworkSettings networkSettings, NetworkObject networkPlayerPrefab)
        {
            networkManager = manager;
            settings = networkSettings;
            playerPrefab = networkPlayerPrefab;
        }
    }
}
