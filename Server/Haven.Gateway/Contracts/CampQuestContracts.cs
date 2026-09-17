namespace Haven.Gateway.Contracts;

public sealed record CampQuestDefinition(string Id, string Title, string ItemId, int Quantity,
    string RewardId, int RewardQuantity, string Condition, int Priority, bool Large, string FallbackDialogue);
public sealed record CampQuestCatalog(int Version, CampQuestDefinition[] Quests);
public sealed record CampQuestWorld(string SessionId, int Day, float Hour, bool HasFirepit, int WallCount,
    int Wood, int Rock, int Bread, float Distance, bool Alive);
public sealed record CampQuestRequest(string RequestId, int CatalogVersion, string Preference, string PlayerMessage,
    CampQuestWorld Context, string[] CompletedItems, CampQuestDefinition[] Candidates);
public sealed record CampQuestProposal(string RequestId, string CandidateId, string Dialogue, string Source);
