using System;
using System.Collections;
using System.Linq;
using Haven.Framework.CampQuests;
using Haven.Framework.Core;
using UnityEngine;

namespace Haven.Hotfix.CampQuests
{
    public sealed class CampQuestService : ICampQuestService
    {
        private readonly CampQuestCatalog _catalog;
        private readonly ICampQuestWorldBridge _world;
        private readonly ICampQuestGenerator _generator;
        private readonly IEventBus _events;
        private CampQuestSave _save = new CampQuestSave();
        private string _session;
        private string _loadError;
        private bool _busy;
        private bool _disposed;
        private int _generation;

        public CampQuestService(CampQuestCatalog catalog, ICampQuestWorldBridge world,
            ICampQuestGenerator generator, IEventBus events)
        {
            CampQuestRules.ValidateCatalog(catalog);
            _catalog = catalog;
            _world = world;
            _generator = generator;
            _events = events;
        }

        public CampQuestView GetView()
        {
            var state = Refresh();
            return new CampQuestView
            {
                World = state, Active = Copy(_save.active), Offer = Copy(_save.offer), Busy = _busy,
                Error = _loadError, CompletedToday = _save.completedItems.Length,
                ContributedWood = _save.contributedWood, ContributedRock = _save.contributedRock
            };
        }

        public IEnumerator Propose(string preference, string message, Action<FrameworkResult> completed)
        {
            var state = Refresh();
            var check = CanInteract(state);
            if (!check.Succeeded) { completed?.Invoke(check); yield break; }
            if (_busy) { completed?.Invoke(Fail("BUSY", "管事正在整理委托，请稍候。")); yield break; }
            if (_save.active != null || _save.offer != null) { completed?.Invoke(FrameworkResult.Success()); yield break; }
            message = (message ?? string.Empty).Trim();
            if (message.Length > 120 || !new[] { "easy", "urgent", "more" }.Contains(preference))
            { completed?.Invoke(Fail("INPUT", "请选择任务偏好，输入不超过 120 字。")); yield break; }
            var candidates = CampQuestRules.Candidates(_catalog, state, _save.completedItems, preference);
            if (candidates.Length == 0) { completed?.Invoke(Fail("DAILY_LIMIT", "今天的木材和石料委托已经完成，明天再来吧。")); yield break; }
            var request = new CampQuestRequest
            {
                requestId = Guid.NewGuid().ToString("N"), catalogVersion = _catalog.version,
                context = state, preference = preference, playerMessage = message,
                completedItems = (string[])_save.completedItems.Clone(), candidates = candidates
            };
            _busy = true;
            var generation = ++_generation;
            FrameworkResult<CampQuestProposal> result = default;
            Exception exception = null;
            try
            {
                yield return SafeCoroutine.Run(_generator.Generate(request, value => result = value), value => exception = value);
                var current = Refresh();
                if (_disposed || generation != _generation || current == null || current.sessionId != state.sessionId ||
                    !current.alive || current.day != state.day)
                { completed?.Invoke(Fail("STALE", "游戏状态已变化，请重新询问。")); yield break; }

                var proposal = result.Succeeded && exception == null && CampQuestRules.ValidProposal(result.Value, request)
                    ? result.Value : new CampQuestProposal
                    {
                        requestId = request.requestId, candidateId = candidates[0].id,
                        dialogue = candidates[0].fallbackDialogue, source = "local-fallback"
                    };
                var selected = candidates.First(q => q.id == proposal.candidateId);
                if (!CampQuestRules.Eligible(selected, current))
                { completed?.Invoke(Fail("STALE", "营地需求已改变，请重新询问。")); yield break; }
                var next = CloneSave();
                next.offer = new CampQuestRecord
                {
                    questId = request.requestId, offeredDay = current.day, definition = selected,
                    dialogue = proposal.dialogue.Trim(), source = proposal.source
                };
                completed?.Invoke(Commit(next, null, proposal.source == "deepseek" ? "管事为你准备了一项委托。" : "已使用营地本地委托。"));
            }
            finally
            {
                if (generation == _generation) _busy = false;
            }
        }

        public FrameworkResult Accept()
        {
            var state = Refresh();
            var check = CanInteract(state);
            if (!check.Succeeded) return check;
            if (_busy || _save.active != null || _save.offer == null) return Fail("NO_OFFER", "当前没有可接取的委托。");
            if (_save.completedItems.Contains(_save.offer.definition.itemId) || !CampQuestRules.Eligible(_save.offer.definition, state))
            {
                var invalid = CloneSave();
                invalid.offer = null;
                var cleared = Commit(invalid, null, "营地需求已变化。");
                return cleared.Succeeded ? Fail("STALE", "营地需求已变化，请重新询问。") : cleared;
            }
            var next = CloneSave();
            next.active = next.offer;
            next.offer = null;
            return Commit(next, null, "已接取委托。交付物资时请回到管事身边。");
        }

        public FrameworkResult Deliver()
        {
            var state = Refresh();
            var check = CanInteract(state);
            if (!check.Succeeded) return check;
            if (_busy || _save.active == null) return Fail("NO_ACTIVE", "没有需要交付的委托。");
            var definition = _save.active.definition;
            if (_save.completedItems.Contains(definition.itemId)) return Fail("DAILY_LIMIT", "今天已经交付过这类物资。");
            if ((definition.itemId == "wood" ? state.wood : state.rock) < definition.quantity)
                return Fail("MATERIALS", "随身物资不足，请继续采集。");
            var next = CloneSave();
            next.completedItems = next.completedItems.Concat(new[] { definition.itemId }).ToArray();
            next.lastCompletedQuestId = next.active.questId;
            next.active = null;
            if (definition.itemId == "wood") next.contributedWood += definition.quantity;
            else next.contributedRock += definition.quantity;
            return Commit(next, definition, $"委托完成！获得面包 ×{definition.rewardQuantity}。物资已计入营地储备。");
        }

        public FrameworkResult Abandon()
        {
            var state = Refresh();
            var check = CanInteract(state);
            if (!check.Succeeded) return check;
            if (_busy) return Fail("BUSY", "请等待本次询问结束。");
            var next = CloneSave();
            next.active = null;
            next.offer = null;
            return Commit(next, null, "已放弃这项委托，没有扣除任何材料。");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            _busy = false;
            _generator.Cancel();
        }

        private CampQuestWorldState Refresh()
        {
            if (_disposed || !_world.IsReady) return null;
            var state = _world.Capture();
            if (_session != state.sessionId)
            {
                _generator.Cancel();
                _generation++;
                _busy = false;
                _session = state.sessionId;
                _loadError = null;
                try
                {
                    var json = _world.ReadSave();
                    _save = string.IsNullOrEmpty(json) ? new CampQuestSave() : ReadState(json);
                    if (_save == null || _save.version != 1 || _save.completedItems == null ||
                        _save.completedItems.Distinct().Count() != _save.completedItems.Length ||
                        _save.completedItems.Any(item => item != "wood" && item != "rock") ||
                        (_save.active != null && !CampQuestRules.ValidDefinition(_save.active.definition)) ||
                        (_save.offer != null && !CampQuestRules.ValidDefinition(_save.offer.definition)))
                        throw new InvalidOperationException("任务存档数据不兼容。");
                }
                catch (Exception exception)
                {
                    _save = new CampQuestSave();
                    _loadError = "任务存档无法读取，请使用新存档或恢复备份。";
                    GameLog.Error("CampQuests", _loadError, "CAMP_SAVE_INVALID", exception);
                }
            }
            if (_save.completionDay != state.day)
            {
                _save.completionDay = state.day;
                _save.completedItems = Array.Empty<string>();
            }
            if (_save.offer != null && _save.offer.offeredDay != state.day) _save.offer = null;
            return state;
        }

        private FrameworkResult CanInteract(CampQuestWorldState state)
        {
            if (state == null) return Fail("NOT_READY", "营地任务尚未准备好。");
            if (_loadError != null) return Fail("SAVE_INVALID", _loadError);
            if (!state.alive) return Fail("DEAD", "当前无法与管事交互。");
            if (state.distance > 3f) return Fail("DISTANCE", "请走到营地管事 3 米以内。");
            return FrameworkResult.Success();
        }

        private FrameworkResult Commit(CampQuestSave next, CampQuestDefinition exchange, string message)
        {
            var result = _world.Commit(_session, WriteState(next), exchange);
            if (result.Succeeded)
            {
                _save = next;
                _events.Publish(new CampQuestChanged(message));
            }
            return result;
        }

        private CampQuestSave CloneSave() => ReadState(WriteState(_save));
        private static string WriteState(CampQuestSave state)
        {
            state.hasOffer = state.offer != null;
            state.hasActive = state.active != null;
            return JsonUtility.ToJson(state);
        }
        private static CampQuestSave ReadState(string json)
        {
            var state = JsonUtility.FromJson<CampQuestSave>(json);
            if (state != null)
            {
                if (!state.hasOffer) state.offer = null;
                if (!state.hasActive) state.active = null;
            }
            return state;
        }
        private static CampQuestRecord Copy(CampQuestRecord value) => value == null ? null : JsonUtility.FromJson<CampQuestRecord>(JsonUtility.ToJson(value));
        private static FrameworkResult Fail(string code, string message) => FrameworkResult.Failure(new FrameworkError("CAMP_" + code, message, "CampQuests", true));
    }
}
