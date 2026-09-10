using Haven.Framework.Core;
using Haven.Framework.Services;

namespace Haven.Networking
{
    public static class QuestResponseValidator
    {
        public static FrameworkResult<AiQuestResponse> Validate(AiQuestResponse value)
        {
            value.RequestId = (value.RequestId ?? string.Empty).Trim();
            value.Dialogue = (value.Dialogue ?? string.Empty).Trim();
            value.QuestType = (value.QuestType ?? string.Empty).Trim();
            value.TargetId = (value.TargetId ?? string.Empty).Trim();
            value.RewardId = (value.RewardId ?? string.Empty).Trim();

            if (value.RequestId.Length == 0)
                return Failure("AI_INVALID_REQUEST_ID", "AIGC response does not contain a request id.");
            if (value.Dialogue.Length == 0 || value.Dialogue.Length > 200)
                return Failure("AI_INVALID_DIALOGUE", "AIGC dialogue must contain 1-200 characters.");
            if (value.QuestType != "Collect")
                return Failure("AI_INVALID_QUEST_TYPE", "Only the Collect quest type is allowed in the vertical slice.");
            if (value.TargetId != "Wood" && value.TargetId != "Stone")
                return Failure("AI_INVALID_TARGET", "Quest target is outside the server allow-list.");
            if (value.Count < 1 || value.Count > 10)
                return Failure("AI_INVALID_COUNT", "Quest count must be between 1 and 10.");
            if (value.RewardId != "Food")
                return Failure("AI_INVALID_REWARD", "Quest reward is outside the server allow-list.");

            return FrameworkResult<AiQuestResponse>.Success(value);
        }

        public static AiQuestResponse CreateFallback(string requestId)
        {
            return new AiQuestResponse
            {
                RequestId = requestId,
                Dialogue = "营地急需木材。请收集 3 份木材，我会用食物作为报酬。",
                QuestType = "Collect",
                TargetId = "Wood",
                Count = 3,
                RewardId = "Food",
                Source = AiQuestSource.LocalFallback
            };
        }

        private static FrameworkResult<AiQuestResponse> Failure(string code, string message)
        {
            return FrameworkResult<AiQuestResponse>.Failure(new FrameworkError(code, message, "AIGCValidation"));
        }
    }
}
