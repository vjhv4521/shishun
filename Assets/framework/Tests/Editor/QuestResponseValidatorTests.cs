using Haven.Framework.Services;
using Haven.Networking;
using NUnit.Framework;

namespace Haven.Framework.Tests
{
    public sealed class QuestResponseValidatorTests
    {
        [Test]
        public void Validate_AcceptsAllowedCollectQuest()
        {
            var result = QuestResponseValidator.Validate(new AiQuestResponse
            {
                RequestId = "request-1",
                Dialogue = "请收集木材。",
                QuestType = "Collect",
                TargetId = "Wood",
                Count = 3,
                RewardId = "Food",
                Source = AiQuestSource.DeepSeek
            });

            Assert.IsTrue(result.Succeeded, result.Error?.ToString());
            Assert.AreEqual("Wood", result.Value.TargetId);
        }

        [Test]
        public void Validate_RejectsGeneratedValuesOutsideServerAllowList()
        {
            var result = QuestResponseValidator.Validate(new AiQuestResponse
            {
                RequestId = "request-2",
                Dialogue = "请收集金币。",
                QuestType = "Collect",
                TargetId = "Gold",
                Count = 3,
                RewardId = "Food"
            });

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("AI_INVALID_TARGET", result.Error.Code);
        }

        [Test]
        public void CreateFallback_AlwaysProducesValidDeterministicQuest()
        {
            var fallback = QuestResponseValidator.CreateFallback("fallback-1");
            var result = QuestResponseValidator.Validate(fallback);

            Assert.IsTrue(result.Succeeded, result.Error?.ToString());
            Assert.AreEqual(AiQuestSource.LocalFallback, fallback.Source);
            Assert.AreEqual("fallback-1", fallback.RequestId);
        }
    }
}
