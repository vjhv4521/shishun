using System;
using System.Collections;
using FishNet.Managing;
using Haven.Framework.Composition;
using Haven.Framework.Core;
using Haven.Framework.Services;
using UnityEngine;

namespace Haven.Networking
{
    [DisallowMultipleComponent]
    public sealed class FishNetRuntimeInstaller : MonoBehaviour, IFrameworkServiceInstaller
    {
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private HavenNetworkSettings settings;

        private FrameworkContext _context;
        private FishNetNetworkService _service;

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
            networkManager.TransportManager.Transport.SetMaximumClients(settings.MaximumPlayers + 1);
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

            _service = new FishNetNetworkService(context, networkManager, settings, authenticator);
            context.Services.Register<INetworkService>(_service);
            context.Services.Register<INetworkHostService>(_service);
            context.Services.Register<ILLMService>(_service);

            yield break;
        }

        public void Uninstall()
        {
            if (_context != null)
            {
                _context.Services.Remove<ILLMService>(false);
                _context.Services.Remove<INetworkHostService>(false);
                _context.Services.Remove<INetworkService>(false);
            }
            _service?.Dispose();
            _service = null;
            _context = null;
        }

        public void Configure(NetworkManager manager, HavenNetworkSettings networkSettings)
        {
            networkManager = manager;
            settings = networkSettings;
        }
    }
}
