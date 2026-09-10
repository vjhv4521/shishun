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
    public string Model { get; set; } = "deepseek-v4-flash";
    public int TimeoutSeconds { get; set; } = 15;
    public int MaximumAttempts { get; set; } = 2;
}

public sealed class PatchStorageOptions
{
    public const string SectionName = "PatchStorage";
    public string Root { get; set; } = "../../Build/LocalServer/patches";
}
