using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Haven.Gateway.Contracts;
using Haven.Gateway.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.Configure<DeepSeekOptions>(builder.Configuration.GetSection(DeepSeekOptions.SectionName));
builder.Services.Configure<PatchStorageOptions>(builder.Configuration.GetSection(PatchStorageOptions.SectionName));
builder.Services.AddHttpClient<IDeepSeekClient, DeepSeekClient>();
builder.Services.AddSingleton<QuestGenerationService>();
builder.Services.AddSingleton<CampQuestPolicy>();
builder.Services.AddHttpClient<CampQuestGenerationService>();
builder.Services.AddSingleton<CampNpcChatPolicy>();
builder.Services.AddHttpClient<CampNpcChatGenerationService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("aigc", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();
var patchOptions = app.Services.GetRequiredService<IOptions<PatchStorageOptions>>().Value;
var patchRoot = Path.GetFullPath(patchOptions.Root, app.Environment.ContentRootPath);
Directory.CreateDirectory(patchRoot);

app.UseRateLimiter();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(patchRoot),
    RequestPath = "/patches",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream"
});

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api/v1"))
    {
        await next();
        return;
    }

    var configuredToken = context.RequestServices.GetRequiredService<IOptions<GatewayOptions>>().Value.SharedToken;
    if (string.IsNullOrEmpty(configuredToken))
    {
        await next();
        return;
    }

    var suppliedToken = context.Request.Headers["X-Haven-Gateway-Token"].ToString();
    var valid = suppliedToken.Length == configuredToken.Length &&
                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(suppliedToken), Encoding.UTF8.GetBytes(configuredToken));
    if (!valid)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "invalid_gateway_token" });
        return;
    }

    await next();
});

app.MapGet("/health", (IOptions<DeepSeekOptions> deepSeek) => Results.Ok(new
{
    status = "ok",
    deepSeekConfigured = !string.IsNullOrWhiteSpace(deepSeek.Value.ApiKey),
    patchHosting = true
}));

app.MapPost("/api/v1/quests/generate", async (
    QuestGenerateRequest request,
    QuestGenerationService quests,
    CancellationToken cancellationToken) =>
{
    var validationError = QuestPolicy.ValidateRequest(request);
    if (validationError is not null)
        return Results.BadRequest(new { error = "invalid_request", message = validationError });

    try
    {
        var response = await quests.GenerateAsync(request, cancellationToken);
        return Results.Ok(response);
    }
    catch (InvalidDataException exception)
    {
        return Results.Json(new { error = "invalid_generated_quest", message = exception.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (DeepSeekUnavailableException exception)
    {
        return Results.Json(new { error = "deepseek_unavailable", message = exception.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).RequireRateLimiting("aigc");

app.MapPost("/api/v1/camp-quests/propose", async (
    CampQuestRequest request, CampQuestPolicy policy, CampQuestGenerationService generator, CancellationToken cancellationToken) =>
{
    var error = policy.Validate(request);
    if (error is not null) return Results.BadRequest(new { error = "invalid_camp_request", message = error });
    try
    {
        return Results.Ok(await generator.GenerateAsync(request, cancellationToken));
    }
    catch (InvalidDataException)
    {
        return Results.Json(new { error = "invalid_camp_response" }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (DeepSeekUnavailableException)
    {
        return Results.Json(new { error = "camp_generator_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).RequireRateLimiting("aigc");

app.MapPost("/api/v1/camp-npc/chat", async (
    CampNpcChatRequest request, CampNpcChatPolicy policy, CampNpcChatGenerationService generator, CancellationToken cancellationToken) =>
{
    var error = policy.Validate(request);
    if (error is not null) return Results.BadRequest(new { error = "invalid_camp_chat", message = error });
    try
    {
        return Results.Ok(await generator.GenerateAsync(request, cancellationToken));
    }
    catch (InvalidDataException)
    {
        return Results.Json(new { error = "invalid_camp_chat_response" }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (DeepSeekUnavailableException)
    {
        return Results.Json(new { error = "camp_chat_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).RequireRateLimiting("aigc");

app.Run();

public partial class Program;
