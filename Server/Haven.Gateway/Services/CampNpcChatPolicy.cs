using System.Text.Json;
using Haven.Gateway.Contracts;

namespace Haven.Gateway.Services;

public sealed class CampNpcChatPolicy
{
    private readonly CampQuestPolicy _quests;

    public CampNpcChatPolicy(CampQuestPolicy quests) { _quests = quests; }

    public string? Validate(CampNpcChatRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 64)
            return "Invalid requestId.";
        if (string.IsNullOrWhiteSpace(request.PlayerMessage) || request.PlayerMessage.Length > 120 ||
            HasUnsafeText(request.PlayerMessage)) return "Invalid player message.";
        var world = request.Context;
        if (world is null || string.IsNullOrWhiteSpace(world.SessionId) || world.SessionId.Length > 128 ||
            world.Day < 1 || !float.IsFinite(world.Hour) || world.Hour < 0 || world.Hour >= 24 ||
            world.WallCount < 0 || world.Wood < 0 || world.Rock < 0 || world.Bread < 0 || !world.Alive ||
            !float.IsFinite(world.Distance) || world.Distance < 0 || world.Distance > 3)
            return "Invalid world snapshot.";
        if (request.CompletedToday is < 0 or > 2 || request.ActiveHeld < 0 ||
            request.ActiveQuestId is null || request.ActiveQuestId.Length > 64)
            return "Invalid quest snapshot.";
        if (request.ActiveQuestId.Length == 0)
        {
            if (request.ActiveHeld != 0) return "Invalid held quantity.";
        }
        else
        {
            var active = _quests.Catalog.Quests.FirstOrDefault(q => q.Id == request.ActiveQuestId);
            if (active is null || request.ActiveHeld != (active.ItemId == "wood" ? world.Wood : world.Rock))
                return "Unknown or inconsistent active quest.";
        }
        if (request.History is null || request.History.Length > 6 || request.History.Length % 2 != 0)
            return "Invalid chat history length.";
        for (var index = 0; index < request.History.Length; index++)
        {
            var turn = request.History[index];
            if (turn is null || turn.Role != (index % 2 == 0 ? "player" : "steward") ||
                string.IsNullOrWhiteSpace(turn.Text) || turn.Text.Length > 240 || HasUnsafeText(turn.Text))
                return "Invalid chat history turn.";
        }
        return null;
    }

    public static CampNpcChatResponse Parse(string? content, CampNpcChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("Empty chat content.");
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
            !root.TryGetProperty("reply", out var replyElement) || replyElement.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Generated chat fields do not match the schema.");
        var reply = replyElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(reply) || reply.Length > 240 || HasUnsafeText(reply))
            throw new InvalidDataException("Generated chat reply is invalid.");
        return new CampNpcChatResponse(request.RequestId, reply, "deepseek");
    }

    private static bool HasUnsafeText(string value) => value.Contains('<') || value.Contains('>') ||
        value.Any(character => char.IsControl(character) && character != '\n');
}
