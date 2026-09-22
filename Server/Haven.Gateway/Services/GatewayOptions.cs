namespace Haven.Gateway.Services;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";
    public string SharedToken { get; set; } = string.Empty;
}

public sealed class DeepSeekOptions
{
    public const string SectionName = "DeepSeek";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-flash";
    public int TimeoutSeconds { get; set; } = 15;
    public int MaximumAttempts { get; set; } = 2;
}

public sealed class PatchStorageOptions
{
    public const string SectionName = "PatchStorage";
    public string Root { get; set; } = "../../Build/LocalServer/patches";
    public bool SimulateDownloadFailure { get; set; }
}

public static class PatchRequestPolicy
{
    public static bool ShouldSimulateDownloadFailure(string? requestPath, bool enabled)
    {
        if (!enabled || string.IsNullOrWhiteSpace(requestPath) ||
            !requestPath.StartsWith("/patches/", StringComparison.OrdinalIgnoreCase))
            return false;

        var extension = Path.GetExtension(requestPath);
        return string.Equals(extension, ".rawfile", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".bundle", StringComparison.OrdinalIgnoreCase);
    }
}
