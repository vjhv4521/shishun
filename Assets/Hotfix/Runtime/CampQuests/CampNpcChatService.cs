using System;
using System.Collections;
using System.Collections.Generic;
using Haven.Framework.CampQuests;
using Haven.Framework.Core;

namespace Haven.Hotfix.CampQuests
{
    public sealed class CampNpcChatService : ICampNpcChatService
    {
        private const int MaxHistory = 12;
        private readonly ICampQuestService _quests;
        private readonly ICampNpcChatGenerator _generator;
        private readonly List<CampNpcChatTurn> _history = new List<CampNpcChatTurn>();
        private string _session;
        private int _generation;
        private bool _disposed;
        private bool _busy;

        public CampNpcChatService(ICampQuestService quests, ICampNpcChatGenerator generator)
        {
            _quests = quests ?? throw new ArgumentNullException(nameof(quests));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        }

        public bool Busy => _busy;

        public CampNpcChatTurn[] GetHistory()
        {
            RefreshSession();
            var copy = new CampNpcChatTurn[_history.Count];
            for (var index = 0; index < copy.Length; index++)
                copy[index] = new CampNpcChatTurn { role = _history[index].role, text = _history[index].text };
            return copy;
        }

        public IEnumerator Send(string message, Action<FrameworkResult<CampNpcChatResponse>> completed)
        {
            var view = RefreshSession();
            if (_disposed || view?.World == null || !view.World.alive || view.World.distance > 3f)
            {
                completed?.Invoke(Fail("NOT_READY", "请走到营地管事身边再聊。"));
                yield break;
            }
            message = (message ?? string.Empty).Trim();
            if (message.Length == 0 || message.Length > 120)
            {
                completed?.Invoke(Fail("INPUT", "请输入不超过 120 字的话。"));
                yield break;
            }
            if (_busy)
            {
                completed?.Invoke(Fail("BUSY", "管事正在回答，请稍候。"));
                yield break;
            }

            var active = view.Active?.definition;
            var request = new CampNpcChatRequest
            {
                requestId = Guid.NewGuid().ToString("N"), playerMessage = message,
                context = view.World, activeQuestId = active?.id ?? string.Empty,
                activeHeld = view.Held, completedToday = view.CompletedToday,
                history = LastTurns(6)
            };
            var generation = ++_generation;
            _busy = true;
            FrameworkResult<CampNpcChatResponse> generated = default;
            Exception exception = null;
            try
            {
                yield return SafeCoroutine.Run(_generator.Generate(request, value => generated = value), value => exception = value);
                var current = RefreshSession();
                if (_disposed || generation != _generation || current?.World == null ||
                    current.World.sessionId != request.context.sessionId || !current.World.alive || current.World.distance > 3f)
                {
                    completed?.Invoke(Fail("STALE", "场景或存档已变化，请重新与管事交谈。"));
                    yield break;
                }

                var response = generated.Succeeded && exception == null && ValidReply(generated.Value, request.requestId)
                    ? generated.Value
                    : new CampNpcChatResponse
                    {
                        requestId = request.requestId, reply = Fallback(current, message), source = "local-fallback"
                    };
                _history.Add(new CampNpcChatTurn { role = "player", text = message });
                _history.Add(new CampNpcChatTurn { role = "steward", text = response.reply.Trim() });
                if (_history.Count > MaxHistory) _history.RemoveRange(0, _history.Count - MaxHistory);
                completed?.Invoke(FrameworkResult<CampNpcChatResponse>.Success(response));
            }
            finally
            {
                if (generation == _generation) _busy = false;
            }
        }

        public void Clear()
        {
            _generation++;
            _busy = false;
            _generator.Cancel();
            _history.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Clear();
        }

        private CampQuestView RefreshSession()
        {
            if (_disposed) return null;
            var view = _quests.GetView();
            var session = view.World?.sessionId;
            if (_session != session)
            {
                Clear();
                _session = session;
            }
            return view;
        }

        private CampNpcChatTurn[] LastTurns(int count)
        {
            var start = Math.Max(0, _history.Count - count);
            var result = new CampNpcChatTurn[_history.Count - start];
            for (var index = 0; index < result.Length; index++)
                result[index] = new CampNpcChatTurn { role = _history[start + index].role, text = _history[start + index].text };
            return result;
        }

        private static bool ValidReply(CampNpcChatResponse response, string requestId) => response != null &&
            response.requestId == requestId && response.source == "deepseek" &&
            !string.IsNullOrWhiteSpace(response.reply) && response.reply.Length <= 240 &&
            response.reply.IndexOf('<') < 0 && response.reply.IndexOf('>') < 0;

        private static string Fallback(CampQuestView view, string message)
        {
            if (view.Active != null)
                return $"你接下的“{view.Active.definition.title}”还在。随身已有 {view.Held} 份所需物资，凑齐后带回来交给我就好。";
            if (message.Contains("面包") || message.Contains("吃"))
                return "营地能用面包换回急需的物资。先看看今天的委托，再量力去采集吧。";
            if (!view.World.hasFirepit)
                return "营地还没有篝火，天暗前先备些石料。物资交给我只算储备，篝火还得我们亲手搭。";
            if (view.World.hour >= 16 || view.World.hour < 6)
                return "夜里要守住火光，木料最好留些余量。你也别离营地太远。";
            return "营地还在慢慢站稳脚跟。木料、石料和口粮都要顾着，先做眼前能做的事吧。";
        }

        private static FrameworkResult<CampNpcChatResponse> Fail(string code, string message) =>
            FrameworkResult<CampNpcChatResponse>.Failure(new FrameworkError("CAMP_CHAT_" + code, message, "CampChat", true));
    }
}
