using System;
using System.Collections;
using Haven.Framework.Core;

namespace Haven.Framework.Services
{
    public enum NetworkState
    {
        Idle,
        Starting,
        Connecting,
        Connected,
        Failed,
        Disconnected
    }

    public sealed class NetworkStateMachine
    {
        public NetworkState State { get; private set; } = NetworkState.Idle;

        public bool TryTransition(NetworkState next)
        {
            if (!CanTransition(State, next))
                return false;
            State = next;
            return true;
        }

        public void Reset(NetworkState state = NetworkState.Idle)
        {
            State = state;
        }

        public static bool CanTransition(NetworkState current, NetworkState next)
        {
            if (current == next)
                return false;

            switch (current)
            {
                case NetworkState.Idle:
                    return next == NetworkState.Starting || next == NetworkState.Disconnected;
                case NetworkState.Starting:
                    return next == NetworkState.Connecting || next == NetworkState.Failed || next == NetworkState.Disconnected;
                case NetworkState.Connecting:
                    return next == NetworkState.Connected || next == NetworkState.Failed || next == NetworkState.Disconnected;
                case NetworkState.Connected:
                    return next == NetworkState.Failed || next == NetworkState.Disconnected;
                case NetworkState.Failed:
                    return next == NetworkState.Starting || next == NetworkState.Disconnected || next == NetworkState.Idle;
                case NetworkState.Disconnected:
                    return next == NetworkState.Starting || next == NetworkState.Idle;
                default:
                    return false;
            }
        }
    }

    [Serializable]
    public readonly struct NetworkEndpoint
    {
        public NetworkEndpoint(string host, ushort port)
        {
            Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            Port = port == 0 ? (ushort)7770 : port;
        }

        public string Host { get; }
        public ushort Port { get; }

        public override string ToString()
        {
            return $"{Host}:{Port}";
        }

        public static bool TryParse(string value, ushort defaultPort, out NetworkEndpoint endpoint)
        {
            endpoint = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim();
            if (normalized.Contains("://", StringComparison.Ordinal) ||
                normalized.IndexOfAny(new[] { '/', '\\', ' ', '\t', '\r', '\n' }) >= 0)
                return false;

            var host = normalized;
            var port = defaultPort == 0 ? (ushort)7770 : defaultPort;
            var separator = normalized.LastIndexOf(':');
            if (separator >= 0)
            {
                host = normalized.Substring(0, separator);
                var portText = normalized.Substring(separator + 1);
                if (string.IsNullOrWhiteSpace(host) || !ushort.TryParse(portText, out port) || port == 0)
                    return false;
            }

            if (host.Length > 253 || host.StartsWith(".", StringComparison.Ordinal) || host.EndsWith(".", StringComparison.Ordinal))
                return false;
            for (var index = 0; index < host.Length; index++)
            {
                var character = host[index];
                if (!char.IsLetterOrDigit(character) && character != '.' && character != '-')
                    return false;
            }

            endpoint = new NetworkEndpoint(host, port);
            return true;
        }
    }

    public readonly struct NetworkStateChanged
    {
        public NetworkStateChanged(NetworkState previous, NetworkState current, string reason = null)
        {
            Previous = previous;
            Current = current;
            Reason = reason ?? string.Empty;
        }

        public NetworkState Previous { get; }
        public NetworkState Current { get; }
        public string Reason { get; }
    }

    public readonly struct NetworkPeerCountChanged
    {
        public NetworkPeerCountChanged(int count)
        {
            Count = Math.Max(0, count);
        }

        public int Count { get; }
    }

    public interface INetworkService
    {
        NetworkState State { get; }
        bool IsConnected { get; }
        int ConnectedPeerCount { get; }
        NetworkEndpoint ConnectedEndpoint { get; }
        FrameworkError LastError { get; }
        IEnumerator Connect(NetworkEndpoint endpoint, Action<FrameworkResult> completed);
        void Disconnect();
    }

    public interface INetworkHostService
    {
        bool IsHosting { get; }
        NetworkEndpoint ShareEndpoint { get; }
        int MaximumPlayers { get; }
        IEnumerator StartHost(NetworkEndpoint endpoint, Action<FrameworkResult> completed);
        void StopHost();
    }

    public enum AiQuestSource
    {
        DeepSeek,
        LocalFallback
    }

    [Serializable]
    public struct AiQuestRequest
    {
        public string NpcId;
        public string PlayerMessage;

        public AiQuestRequest(string npcId, string playerMessage)
        {
            NpcId = npcId ?? string.Empty;
            PlayerMessage = playerMessage ?? string.Empty;
        }
    }

    [Serializable]
    public struct AiQuestResponse
    {
        public string RequestId;
        public string Dialogue;
        public string QuestType;
        public string TargetId;
        public int Count;
        public string RewardId;
        public AiQuestSource Source;
    }

    public readonly struct AiQuestCompleted
    {
        public AiQuestCompleted(AiQuestResponse response)
        {
            Response = response;
        }

        public AiQuestResponse Response { get; }
    }

    public readonly struct AiQuestFailed
    {
        public AiQuestFailed(FrameworkError error)
        {
            Error = error;
        }

        public FrameworkError Error { get; }
    }

    public interface ILLMService
    {
        IEnumerator RequestQuest(AiQuestRequest request, Action<FrameworkResult<AiQuestResponse>> completed);
    }
}
