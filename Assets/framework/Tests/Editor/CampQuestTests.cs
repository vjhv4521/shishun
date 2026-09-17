using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Haven.Framework.CampQuests;
using Haven.Framework.Core;
using Haven.Hotfix.CampQuests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Haven.Framework.Tests
{
    public sealed class CampQuestTests
    {
        private CampQuestCatalog _catalog;
        private World _world;
        private Generator _generator;
        private CampQuestService _service;

        [SetUp]
        public void Setup()
        {
            _catalog = JsonUtility.FromJson<CampQuestCatalog>(File.ReadAllText("Assets/Hotfix/Content/CampQuestCatalog.json"));
            _world = new World();
            _generator = new Generator();
            _service = new CampQuestService(_catalog, _world, _generator, new EventBus());
        }

        [TearDown] public void Teardown() { _service.Dispose(); }

        [Test]
        public void NightAndBuildingsChangeEligibleCandidates()
        {
            var state = _world.State;
            Assert.That(Candidates().Any(q => q.condition == "no_fire"), Is.True);
            Assert.That(Candidates().Any(q => q.condition == "night_fire"), Is.False);
            state.hasFirepit = true; state.hour = 18; state.wallCount = 2;
            Assert.That(Candidates().Any(q => q.condition == "no_fire"), Is.False);
            Assert.That(Candidates().Any(q => q.condition == "few_walls"), Is.False);
            Assert.That(Candidates().First().condition, Is.EqualTo("night_fire"));
            state.hour = 10;
            Assert.That(Candidates().Any(q => q.condition == "night_fire"), Is.False);
        }

        [Test]
        public void PreferenceAndCompletionFilterBeforeCallingModel()
        {
            Assert.That(Candidates("easy").Any(q => q.large), Is.False);
            Assert.That(Candidates("more").First().large, Is.True);
            Assert.That(CampQuestRules.Candidates(_catalog, _world.State, new[] { "wood", "rock" }, "urgent"), Is.Empty);
        }

        [Test]
        public void InvalidAndDuplicateCatalogIdsAreRejected()
        {
            _catalog.quests[1].id = _catalog.quests[0].id;
            Assert.Throws<ArgumentException>(() => CampQuestRules.ValidateCatalog(_catalog));
            _catalog.quests[1].id = "restored";
            _catalog.quests[0].itemId = "gold";
            Assert.Throws<ArgumentException>(() => CampQuestRules.ValidateCatalog(_catalog));
        }

        [Test]
        public void UnavailableModelFallsBackAndOpeningReusesProposal()
        {
            Propose();
            Assert.That(_service.GetView().Offer.source, Is.EqualTo("local-fallback"));
            Propose();
            Assert.That(_generator.Calls, Is.EqualTo(1));
        }

        [TestCase("forged")]
        [TestCase("wrong-request")]
        [TestCase("empty")]
        public void InvalidModelOutputFallsBack(string mode)
        {
            _generator.Mode = mode;
            Propose();
            Assert.That(_service.GetView().Offer.source, Is.EqualTo("local-fallback"));
        }

        [Test]
        public void ValidGeneratedCandidateIsUsed()
        {
            _generator.Mode = "valid";
            Propose();
            Assert.That(_service.GetView().Offer.source, Is.EqualTo("deepseek"));
        }

        [Test]
        public void InsufficientMaterialsAndDistanceRejectDelivery()
        {
            Propose(); Assert.That(_service.Accept().Succeeded, Is.True);
            Assert.That(_service.Deliver().Error.Code, Is.EqualTo("CAMP_MATERIALS"));
            _world.State.rock = 8; _world.State.wood = 8; _world.State.distance = 4;
            Assert.That(_service.Deliver().Error.Code, Is.EqualTo("CAMP_DISTANCE"));
        }

        [Test]
        public void SaveFailureKeepsActiveQuestAndRetryPaysOnlyOnce()
        {
            Propose(); _service.Accept();
            _world.State.rock = 8; _world.State.wood = 8;
            _world.FailCommit = true;
            Assert.That(_service.Deliver().Succeeded, Is.False);
            Assert.That(_service.GetView().Active, Is.Not.Null);
            Assert.That(_world.State.bread, Is.Zero);
            _world.FailCommit = false;
            Assert.That(_service.Deliver().Succeeded, Is.True);
            Assert.That(_service.Deliver().Succeeded, Is.False);
            Assert.That(_world.State.bread, Is.EqualTo(1));
        }

        [Test]
        public void CompletionSurvivesRecreationAndBlocksSameMaterial()
        {
            Propose(); _service.Accept(); _world.State.rock = 8; _world.State.wood = 8;
            var item = _service.GetView().Active.definition.itemId;
            _service.Deliver();
            _service.Dispose();
            _service = new CampQuestService(_catalog, _world, _generator, new EventBus());
            Assert.That(_service.GetView().CompletedToday, Is.EqualTo(1));
            Propose();
            Assert.That(_service.GetView().Offer.definition.itemId, Is.Not.EqualTo(item));
        }

        [Test]
        public void ActiveQuestSurvivesMidnightButUnacceptedOfferExpires()
        {
            Propose(); _world.State.day++;
            Assert.That(_service.GetView().Offer, Is.Null);
            Propose(); _service.Accept(); var id = _service.GetView().Active.questId;
            _world.State.day++;
            Assert.That(_service.GetView().Active.questId, Is.EqualTo(id));
        }

        [Test]
        public void AbandonDoesNotConsumeAndAllowsNewProposal()
        {
            Propose(); _service.Accept(); _world.State.wood = 5;
            Assert.That(_service.Abandon().Succeeded, Is.True);
            Assert.That(_world.State.wood, Is.EqualTo(5));
            Propose(); Assert.That(_generator.Calls, Is.EqualTo(2));
        }

        [Test]
        public void CorruptSaveFailsClosed()
        {
            _world.Json = "{\"version\":999}";
            LogAssert.Expect(LogType.Error, new Regex("CAMP_SAVE_INVALID"));
            Assert.That(_service.GetView().Error, Is.Not.Empty);
            Assert.That(_service.Accept().Succeeded, Is.False);
            Assert.That(_world.Json, Does.Contain("999"));
        }

        [Test]
        public void LateResultAfterSaveSwitchOrDeathIsIgnored()
        {
            _generator.BeforeComplete = () => _world.State.sessionId = "another-save";
            Assert.That(Propose().Succeeded, Is.False);
            Assert.That(_service.GetView().Offer, Is.Null);
            _generator.BeforeComplete = () => _world.State.alive = false;
            Assert.That(Propose().Succeeded, Is.False);
            Assert.That(_service.GetView().Offer, Is.Null);
        }

        private CampQuestDefinition[] Candidates(string preference = "urgent") => CampQuestRules.Candidates(_catalog, _world.State, Array.Empty<string>(), preference);
        private FrameworkResult Propose()
        {
            FrameworkResult result = default;
            var routine = SafeCoroutine.Run(_service.Propose("easy", "", value => result = value), exception => throw exception);
            while (routine.MoveNext()) { }
            return result;
        }

        private sealed class World : ICampQuestWorldBridge
        {
            public CampQuestWorldState State = new CampQuestWorldState { sessionId = "test", day = 1, hour = 8, alive = true, distance = 1 };
            public string Json;
            public bool FailCommit;
            public bool IsReady => true;
            public CampQuestWorldState Capture() => JsonUtility.FromJson<CampQuestWorldState>(JsonUtility.ToJson(State));
            public string ReadSave() => Json;
            public FrameworkResult Commit(string sessionId, string saveJson, CampQuestDefinition exchange = null)
            {
                if (FailCommit) return FrameworkResult.Failure(new FrameworkError("SAVE", "test failure", "test"));
                if (exchange != null)
                {
                    if (exchange.itemId == "wood") State.wood -= exchange.quantity;
                    else State.rock -= exchange.quantity;
                    State.bread += exchange.rewardQuantity;
                }
                Json = saveJson;
                return FrameworkResult.Success();
            }
        }

        private sealed class Generator : ICampQuestGenerator
        {
            public int Calls;
            public string Mode;
            public Action BeforeComplete;
            public IEnumerator Generate(CampQuestRequest request, Action<FrameworkResult<CampQuestProposal>> completed)
            {
                Calls++;
                yield return null;
                BeforeComplete?.Invoke();
                if (Mode != null)
                    completed(FrameworkResult<CampQuestProposal>.Success(new CampQuestProposal
                    {
                        requestId = Mode == "wrong-request" ? "wrong" : request.requestId,
                        candidateId = Mode == "forged" ? "gold" : request.candidates[0].id,
                        dialogue = Mode == "empty" ? "" : "请为营地准备些物资。", source = "deepseek"
                    }));
                else completed(FrameworkResult<CampQuestProposal>.Failure(new FrameworkError("UNAVAILABLE", "offline", "test")));
            }
            public void Cancel() { }
            public void Dispose() { }
        }
    }
}
