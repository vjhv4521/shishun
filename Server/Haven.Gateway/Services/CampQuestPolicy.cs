using System.Text.Json;
using Haven.Gateway.Contracts;

namespace Haven.Gateway.Services;

public sealed class CampQuestPolicy
{
    public CampQuestCatalog Catalog { get; }

    public CampQuestPolicy()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Content", "CampQuestCatalog.json");
        Catalog = JsonSerializer.Deserialize<CampQuestCatalog>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Camp quest catalog is missing.");
        if (Catalog.Version != 1 || Catalog.Quests.Length == 0 ||
            Catalog.Quests.Select(q => q.Id).Distinct(StringComparer.Ordinal).Count() != Catalog.Quests.Length ||
            Catalog.Quests.Any(q => (q.ItemId != "wood" && q.ItemId != "rock") || q.RewardId != "bread" ||
                q.Quantity != (q.ItemId == "wood" ? (q.Large ? 5 : 3) : (q.Large ? 4 : 2)) ||
                q.RewardQuantity != (q.Large ? 2 : 1) ||
                !new[] { "always", "no_fire", "night_fire", "few_walls" }.Contains(q.Condition)))
            throw new InvalidDataException("Camp quest catalog failed validation.");
    }

    public string? Validate(CampQuestRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 64)
            return "Invalid requestId.";
        if (request.CatalogVersion != Catalog.Version) return "Unsupported catalogVersion.";
        if (!new[] { "easy", "urgent", "more" }.Contains(request.Preference)) return "Invalid preference.";
        if (request.PlayerMessage is null || request.PlayerMessage.Length > 120) return "Message exceeds 120 characters.";
        var world = request.Context;
        if (world is null || string.IsNullOrWhiteSpace(world.SessionId) || world.SessionId.Length > 128 ||
            world.Day < 1 || !float.IsFinite(world.Hour) || world.Hour < 0 || world.Hour >= 24 ||
            world.WallCount < 0 || world.Wood < 0 || world.Rock < 0 || world.Bread < 0 || !world.Alive ||
            !float.IsFinite(world.Distance) || world.Distance < 0 || world.Distance > 3)
            return "Invalid world snapshot.";
        if (request.CompletedItems is null || request.CompletedItems.Length > 2 ||
            request.CompletedItems.Distinct().Count() != request.CompletedItems.Length ||
            request.CompletedItems.Any(item => item != "wood" && item != "rock"))
            return "Invalid completion history.";
        if (request.Candidates is null || request.Candidates.Length == 0 || request.Candidates.Length > Catalog.Quests.Length)
            return "Invalid candidates.";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var proposed in request.Candidates)
        {
            if (proposed is null || !ids.Add(proposed.Id)) return "Duplicate or empty candidate.";
            var known = Catalog.Quests.FirstOrDefault(q => q.Id == proposed.Id);
            if (known is null || known != proposed) return "Candidate differs from the gateway catalog.";
            if (request.CompletedItems.Contains(known.ItemId) || request.Preference == "easy" && known.Large || !Eligible(known, world))
                return "Candidate is not currently eligible.";
        }
        return null;
    }

    public static bool Eligible(CampQuestDefinition quest, CampQuestWorld world) => quest.Condition switch
    {
        "night_fire" => world.HasFirepit && (world.Hour >= 16 || world.Hour < 6),
        "no_fire" => !world.HasFirepit,
        "few_walls" => world.WallCount < 2,
        "always" => true,
        _ => false
    };

    public static CampQuestProposal Parse(string? content, CampQuestRequest request)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("Empty generated content.");
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
            !root.TryGetProperty("candidateId", out var candidate) || candidate.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("dialogue", out var dialogueElement) || dialogueElement.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Generated fields do not match the camp quest schema.");
        var id = candidate.GetString();
        var dialogue = dialogueElement.GetString()?.Trim();
        if (!request.Candidates.Any(q => q.Id == id) || string.IsNullOrWhiteSpace(dialogue) ||
            dialogue.Length > 200 || dialogue.Contains('<') || dialogue.Contains('>'))
            throw new InvalidDataException("Generated candidate or dialogue is invalid.");
        return new CampQuestProposal(request.RequestId, id!, dialogue, "deepseek");
    }
}
