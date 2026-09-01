using System;
using System.Collections;
using Haven.Framework.Core;

namespace Haven.Hotfix.Services
{
    public interface INetworkService
    {
        bool IsConnected { get; }
        IEnumerator Connect(string endpoint, Action<FrameworkResult> completed);
        void Disconnect();
    }

    public interface ILLMService
    {
        IEnumerator Request(string requestJson, Action<FrameworkResult<string>> completed);
    }

    public sealed class OfflineNetworkService : INetworkService
    {
        public bool IsConnected { get; private set; }

        public IEnumerator Connect(string endpoint, Action<FrameworkResult> completed)
        {
            IsConnected = true;
            completed?.Invoke(FrameworkResult.Success());
            yield break;
        }

        public void Disconnect()
        {
            IsConnected = false;
        }
    }

    public sealed class LocalFallbackLlmService : ILLMService
    {
        private const string FallbackJson = "{\"dialogue\":\"通信暂不可用，请先收集基础物资。\",\"questType\":\"Collect\",\"targetId\":\"Wood\",\"count\":3,\"rewardId\":\"Food\"}";

        public IEnumerator Request(string requestJson, Action<FrameworkResult<string>> completed)
        {
            completed?.Invoke(FrameworkResult<string>.Success(FallbackJson));
            yield break;
        }
    }
}
