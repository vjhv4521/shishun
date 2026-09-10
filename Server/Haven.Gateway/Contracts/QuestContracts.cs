namespace Haven.Gateway.Contracts;

public sealed record QuestGenerateRequest(
    string RequestId,
    string NpcId,
    string PlayerMessage,
    QuestContext Context);

public sealed record QuestContext(
    int Day,
    string[] AllowedTargets,
    string[] AllowedRewards);

public sealed record QuestGenerateResponse(
    string RequestId,
    string Dialogue,
    string QuestType,
    string TargetId,
    int Count,
    string RewardId,
    string Source);

public sealed record DeepSeekQuestPayload(
    string Dialogue,
    string QuestType,
    string TargetId,
    int Count,
    string RewardId);
