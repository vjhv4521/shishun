using Haven.Gateway.Contracts;

namespace Haven.Gateway.Services;

public static class QuestPolicy
{
    private static readonly HashSet<string> ServerTargets = new(StringComparer.Ordinal) { "Wood", "Stone" };
    private static readonly HashSet<string> ServerRewards = new(StringComparer.Ordinal) { "Food" };

    public static string? ValidateRequest(QuestGenerateRequest? request)
    {
        if (request is null)
            return "Request body is required.";
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 64)
            return "requestId must contain 1-64 characters.";
        if (string.IsNullOrWhiteSpace(request.NpcId) || request.NpcId.Length > 64)
            return "npcId must contain 1-64 characters.";
        if (string.IsNullOrWhiteSpace(request.PlayerMessage) || request.PlayerMessage.Length > 240)
            return "playerMessage must contain 1-240 characters.";
        if (request.Context is null)
            return "context is required.";
        if (request.Context.Day < 1)
            return "context.day must be positive.";
        if (!HasAllowedValue(request.Context.AllowedTargets, ServerTargets))
            return "context.allowedTargets contains no server-approved target.";
        if (!HasAllowedValue(request.Context.AllowedRewards, ServerRewards))
            return "context.allowedRewards contains no server-approved reward.";
        return null;
    }

    public static string? ValidateGenerated(DeepSeekQuestPayload? quest, QuestGenerateRequest request)
    {
        if (quest is null)
            return "DeepSeek response is empty.";
        if (string.IsNullOrWhiteSpace(quest.Dialogue) || quest.Dialogue.Length > 200)
            return "dialogue must contain 1-200 characters.";
        if (!string.Equals(quest.QuestType, "Collect", StringComparison.Ordinal))
            return "questType must be Collect.";
        if (!ServerTargets.Contains(quest.TargetId) || !request.Context.AllowedTargets.Contains(quest.TargetId, StringComparer.Ordinal))
            return "targetId is outside the server allow-list.";
        if (quest.Count is < 1 or > 10)
            return "count must be between 1 and 10.";
        if (!ServerRewards.Contains(quest.RewardId) || !request.Context.AllowedRewards.Contains(quest.RewardId, StringComparer.Ordinal))
            return "rewardId is outside the server allow-list.";
        return null;
    }

    private static bool HasAllowedValue(IEnumerable<string>? values, HashSet<string> serverValues)
    {
        return values is not null && values.Any(serverValues.Contains);
    }
}
