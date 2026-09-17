namespace Haven.Gateway.Contracts;

public sealed record CampNpcChatTurn(string Role, string Text);

public sealed record CampNpcChatRequest(string RequestId, string PlayerMessage, CampQuestWorld Context,
    string ActiveQuestId, int ActiveHeld, int CompletedToday, CampNpcChatTurn[] History);

public sealed record CampNpcChatResponse(string RequestId, string Reply, string Source);
