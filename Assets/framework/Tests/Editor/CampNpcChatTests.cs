using System;
using System.Collections;
using Haven.Framework.CampQuests;
using Haven.Framework.Core;
using Haven.Hotfix.CampQuests;
using NUnit.Framework;

namespace Haven.Framework.Tests
{
    public sealed class CampNpcChatTests
    {
        private Quests _quests;
        private Generator _generator;
        private CampNpcChatService _service;

        [SetUp]
        public void Setup()
        {
            _quests = new Quests();
            _generator = new Generator();
            _service = new CampNpcChatService(_quests, _generator);
        }

        [TearDown] public void Teardown() { _service.Dispose(); }

        [Test]
        public void OfflineChatUsesWorldStateWithoutTouchingQuestActions()
        {
            var response = Send("今晚怎么办？");
            Assert.That(response.Succeeded, Is.True);
            Assert.That(response.Value.source, Is.EqualTo("local-fallback"));
            Assert.That(response.Value.reply, Does.Contain("篝火"));
            Assert.That(_generator.Last.context.day, Is.EqualTo(2));
            Assert.That(_generator.Last.context.hasFirepit, Is.False);
            Assert.That(_generator.Last.history, Is.Empty);
            Assert.That(_quests.Actions, Is.Zero);
            Assert.That(_service.GetHistory().Length, Is.EqualTo(2));
        }

        [Test]
        public void ValidAnswerAndOnlySixRecentTurnsAreSent()
        {
            _generator.Mode = "valid";
            for (var index = 0; index < 5; index++) Assert.That(Send("第 " + index + " 次").Succeeded, Is.True);
            Assert.That(_generator.Last.history.Length, Is.EqualTo(6));
            Assert.That(_generator.Last.history[0].role, Is.EqualTo("player"));
            Assert.That(_service.GetHistory().Length, Is.EqualTo(10));
            Assert.That(_service.GetHistory()[9].text, Is.EqualTo("先在营地备些石料。"));
        }

        [TestCase("wrong-request")]
        [TestCase("markup")]
        [TestCase("empty")]
        public void InvalidGatewayAnswerUsesLocalFallback(string mode)
        {
            _generator.Mode = mode;
            Assert.That(Send("营地有什么需要？").Value.source, Is.EqualTo("local-fallback"));
        }

        [Test]
        public void ChangingSaveDiscardsLateResultAndConversation()
        {
            _generator.BeforeComplete = () => _quests.View.World.sessionId = "new-save";
            var response = Send("还有什么要做？");
            Assert.That(response.Succeeded, Is.False);
            Assert.That(response.Error.Code, Is.EqualTo("CAMP_CHAT_STALE"));
            Assert.That(_service.GetHistory(), Is.Empty);
        }

        [Test]
        public void DistanceAndInputLimitsAreEnforced()
        {
            Assert.That(Send(" ").Error.Code, Is.EqualTo("CAMP_CHAT_INPUT"));
            Assert.That(Send(new string('x', 121)).Error.Code, Is.EqualTo("CAMP_CHAT_INPUT"));
            _quests.View.World.distance = 4;
            Assert.That(Send("你好").Error.Code, Is.EqualTo("CAMP_CHAT_NOT_READY"));
            Assert.That(_generator.Calls, Is.Zero);
        }

        private FrameworkResult<CampNpcChatResponse> Send(string message)
        {
            FrameworkResult<CampNpcChatResponse> result = default;
            var routine = SafeCoroutine.Run(_service.Send(message, value => result = value), exception => throw exception);
            while (routine.MoveNext()) { }
            return result;
        }

        private sealed class Quests : ICampQuestService
        {
            public int Actions;
            public CampQuestView View = new CampQuestView
            {
                World = new CampQuestWorldState { sessionId = "save-1", day = 2, hour = 18, alive = true, distance = 1 }
            };
            public CampQuestView GetView() => View;
            public IEnumerator Propose(string preference, string message, Action<FrameworkResult> completed)
            { Actions++; yield break; }
            public FrameworkResult Accept() { Actions++; return FrameworkResult.Success(); }
            public FrameworkResult Deliver() { Actions++; return FrameworkResult.Success(); }
            public FrameworkResult Abandon() { Actions++; return FrameworkResult.Success(); }
            public void Dispose() { }
        }

        private sealed class Generator : ICampNpcChatGenerator
        {
            public int Calls;
            public string Mode;
            public CampNpcChatRequest Last;
            public Action BeforeComplete;
            public IEnumerator Generate(CampNpcChatRequest request, Action<FrameworkResult<CampNpcChatResponse>> completed)
            {
                Calls++;
                Last = request;
                yield return null;
                BeforeComplete?.Invoke();
                if (Mode == null)
                    completed(FrameworkResult<CampNpcChatResponse>.Failure(new FrameworkError("OFFLINE", "offline", "test")));
                else completed(FrameworkResult<CampNpcChatResponse>.Success(new CampNpcChatResponse
                {
                    requestId = Mode == "wrong-request" ? "wrong" : request.requestId,
                    reply = Mode == "empty" ? "" : Mode == "markup" ? "<b>你好</b>" : "先在营地备些石料。",
                    source = "deepseek"
                }));
            }
            public void Cancel() { }
            public void Dispose() { }
        }
    }
}
