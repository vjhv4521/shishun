using System;
using System.Collections;
using Haven.Framework.Core;
using Haven.Framework.Services;

namespace Haven.Hotfix.Services
{
    public sealed class OfflineNetworkService : INetworkService
    {
        public NetworkState State { get; private set; } = NetworkState.Idle;
        public bool IsConnected { get; private set; }
        public int ConnectedPeerCount => IsConnected ? 1 : 0;
        public NetworkEndpoint ConnectedEndpoint { get; private set; }
        public FrameworkError LastError => null;

        public IEnumerator Connect(NetworkEndpoint endpoint, Action<FrameworkResult> completed)
        {
            State = NetworkState.Connected;
            IsConnected = true;
            ConnectedEndpoint = endpoint;
            completed?.Invoke(FrameworkResult.Success());
            yield break;
        }

        public void Disconnect()
        {
            State = NetworkState.Disconnected;
            IsConnected = false;
            ConnectedEndpoint = default;
        }
    }

    public sealed class LocalFallbackLlmService : ILLMService
    {
        public IEnumerator RequestQuest(AiQuestRequest request, Action<FrameworkResult<AiQuestResponse>> completed)
        {
            completed?.Invoke(FrameworkResult<AiQuestResponse>.Success(new AiQuestResponse
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Dialogue = "通信暂不可用，请先收集基础物资。",
                QuestType = "Collect",
                TargetId = "Wood",
                Count = 3,
                RewardId = "Food",
                Source = AiQuestSource.LocalFallback
            }));
            yield break;
        }
    }
}
