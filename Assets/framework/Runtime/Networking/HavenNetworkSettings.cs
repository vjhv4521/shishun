using System;
using UnityEngine;

namespace Haven.Networking
{
    public enum HavenTransportProtocol
    {
        TugboatUdp
    }

    [CreateAssetMenu(fileName = "HavenNetworkSettings", menuName = "Haven/Network Settings")]
    public sealed class HavenNetworkSettings : ScriptableObject
    {
        public const string DefaultResourceName = "HavenNetworkSettings";

        [Header("FishNet")]
        [SerializeField] private HavenTransportProtocol transportProtocol = HavenTransportProtocol.TugboatUdp;
        [SerializeField] private string defaultHost = "127.0.0.1";
        [SerializeField] private ushort port = 7770;
        [SerializeField, Range(2, 4)] private int maximumPlayers = 4;
        [SerializeField, Min(1)] private int protocolVersion = 1;
        [SerializeField, Min(1f)] private float connectTimeoutSeconds = 10f;
        [SerializeField, Min(0.02f)] private float inputSendInterval = 0.05f;
        [SerializeField, Min(0.1f)] private float playerMoveSpeed = 4f;

        [Header("Room scenes")]
        [SerializeField] private string menuSceneName = "MainMenu";
        [SerializeField] private string singlePlayerSceneName = "WorldGenMap";
        [SerializeField] private string multiplayerSceneName = "FrameworkDemo";

        [Header("Server-side AIGC gateway")]
        [SerializeField] private string gatewayBaseUrl = "http://127.0.0.1:5080";
        [SerializeField] private string gatewayTokenEnvironmentVariable = "HAVEN_GATEWAY_TOKEN";
        [SerializeField, Min(3)] private int gatewayTimeoutSeconds = 20;
        [SerializeField, Min(0.5f)] private float aiRequestCooldownSeconds = 2f;

        public HavenTransportProtocol TransportProtocol => transportProtocol;
        public string TransportDisplayName => "FishNet Tugboat (UDP)";
        public string DefaultHost => string.IsNullOrWhiteSpace(defaultHost) ? "127.0.0.1" : defaultHost.Trim();
        public ushort Port => port == 0 ? (ushort)7770 : port;
        public int MaximumPlayers => Mathf.Clamp(maximumPlayers, 2, 4);
        public int ProtocolVersion => Mathf.Max(1, protocolVersion);
        public float ConnectTimeoutSeconds => Mathf.Max(1f, connectTimeoutSeconds);
        public float InputSendInterval => Mathf.Max(0.02f, inputSendInterval);
        public float PlayerMoveSpeed => Mathf.Max(0.1f, playerMoveSpeed);
        public string MenuSceneName => SceneNameOrDefault(menuSceneName, "MainMenu");
        public string SinglePlayerSceneName => SceneNameOrDefault(singlePlayerSceneName, "WorldGenMap");
        public string MultiplayerSceneName => SceneNameOrDefault(multiplayerSceneName, "FrameworkDemo");
        public string GatewayBaseUrl => string.IsNullOrWhiteSpace(gatewayBaseUrl) ? "http://127.0.0.1:5080" : gatewayBaseUrl.TrimEnd('/');
        public int GatewayTimeoutSeconds => Mathf.Max(3, gatewayTimeoutSeconds);
        public float AiRequestCooldownSeconds => Mathf.Max(0.5f, aiRequestCooldownSeconds);

        public string GatewayToken
        {
            get
            {
                if (string.IsNullOrWhiteSpace(gatewayTokenEnvironmentVariable))
                    return string.Empty;
                return Environment.GetEnvironmentVariable(gatewayTokenEnvironmentVariable) ?? string.Empty;
            }
        }

        public static HavenNetworkSettings CreateRuntimeDefault()
        {
            return CreateInstance<HavenNetworkSettings>();
        }

        private static string SceneNameOrDefault(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
