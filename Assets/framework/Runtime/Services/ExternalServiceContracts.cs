using System;
using System.Collections;
using Haven.Framework.Core;

namespace Haven.Framework.Services
{
    public enum NetworkState
    {
        Disconnected,
        Connecting,
        Connected,
        Disconnecting,
        Failed
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
        IEnumerator Connect(NetworkEndpoint endpoint, Action<FrameworkResult> completed);
        void Disconnect();
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
