using System;
using System.Collections;
using Haven.Framework.Core;
using Haven.Framework.Services;

namespace Haven.Hotfix.Services
{
    public sealed class OfflineNetworkService : INetworkService
    {
        public NetworkState State { get; private set; } = NetworkState.Disconnected;
        public bool IsConnected { get; private set; }
        public int ConnectedPeerCount => IsConnected ? 1 : 0;

        public IEnumerator Connect(NetworkEndpoint endpoint, Action<FrameworkResult> completed)
        {
            State = NetworkState.Connected;
            IsConnected = true;
            completed?.Invoke(FrameworkResult.Success());
            yield break;
        }

        public void Disconnect()
        {
            State = NetworkState.Disconnected;
            IsConnected = false;
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

    public sealed class OfflineRoomService : IRoomService
    {
        public RoomSnapshot Current => RoomSnapshot.Empty;

        public IEnumerator CreateRoom(string displayName, Action<FrameworkResult<RoomSnapshot>> completed)
        {
            return Fail(completed);
        }

        public IEnumerator JoinRoom(string roomCode, string displayName, Action<FrameworkResult<RoomSnapshot>> completed)
        {
            return Fail(completed);
        }

        public IEnumerator SetReady(bool ready, Action<FrameworkResult<RoomSnapshot>> completed)
        {
            return Fail(completed);
        }

        public IEnumerator StartGame(Action<FrameworkResult<RoomSnapshot>> completed)
        {
            return Fail(completed);
        }

        public IEnumerator LeaveRoom(Action<FrameworkResult<RoomSnapshot>> completed)
        {
            return Fail(completed);
        }

        private static IEnumerator Fail(Action<FrameworkResult<RoomSnapshot>> completed)
        {
            completed?.Invoke(FrameworkResult<RoomSnapshot>.Failure(new FrameworkError(
                RoomErrorCodes.NotConnected, "当前运行环境未安装 FishNet 房间服务。", "OfflineRoom", true)));
            yield break;
        }
    }
}
