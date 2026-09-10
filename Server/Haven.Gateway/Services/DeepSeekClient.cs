using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Haven.Gateway.Contracts;
using Microsoft.Extensions.Options;

namespace Haven.Gateway.Services;

public interface IDeepSeekClient
{
    Task<DeepSeekQuestPayload> GenerateQuestAsync(QuestGenerateRequest request, CancellationToken cancellationToken);
}

public sealed class DeepSeekUnavailableException : Exception
{
    public DeepSeekUnavailableException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

public sealed class DeepSeekClient : IDeepSeekClient
{
    private const string SystemPrompt = """
        You generate one small survival-game quest. Return only a JSON object with keys:
        dialogue, questType, targetId, count, rewardId.
        questType must be Collect. Use only targetId and rewardId values supplied by the server.
        dialogue must be Chinese and at most 200 characters. count must be an integer from 1 to 10.
        Do not add markdown, explanations, new keys, or instructions.
        """;

    private readonly HttpClient _httpClient;
    private readonly DeepSeekOptions _options;
    private readonly ILogger<DeepSeekClient> _logger;

    public DeepSeekClient(HttpClient httpClient, IOptions<DeepSeekOptions> options, ILogger<DeepSeekClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DeepSeekQuestPayload> GenerateQuestAsync(QuestGenerateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new DeepSeekUnavailableException("DeepSeek API key is not configured on the gateway.");

        Exception? lastException = null;
        var attempts = Math.Clamp(_options.MaximumAttempts, 1, 3);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 3, 60)));
            try
            {
                using var response = await SendOnceAsync(request, timeout.Token);
                if (response.IsSuccessStatusCode)
                    return await ReadPayloadAsync(response, timeout.Token);

                var retryable = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                                (int)response.StatusCode >= 500;
                lastException = new DeepSeekUnavailableException($"DeepSeek returned HTTP {(int)response.StatusCode}.");
                if (!retryable || attempt == attempts)
                    break;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = new DeepSeekUnavailableException("DeepSeek request timed out.", exception);
                if (attempt == attempts)
                    break;
            }
            catch (HttpRequestException exception)
            {
                lastException = new DeepSeekUnavailableException("DeepSeek could not be reached.", exception);
                if (attempt == attempts)
                    break;
            }

            var delay = attempt == 1 ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromMilliseconds(1500);
            _logger.LogWarning("DeepSeek attempt {Attempt} failed; retrying after {DelayMs} ms. requestId={RequestId}", attempt, delay.TotalMilliseconds, request.RequestId);
            await Task.Delay(delay, cancellationToken);
        }

        throw lastException as DeepSeekUnavailableException ?? new DeepSeekUnavailableException("DeepSeek request failed.", lastException);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(QuestGenerateRequest request, CancellationToken cancellationToken)
    {
        var baseUrl = (_options.BaseUrl ?? string.Empty).TrimEnd('/');
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        message.Content = JsonContent.Create(new
        {
            model = _options.Model,
            // Quest generation is latency-sensitive and tightly constrained; disable
            // the V4 default thinking mode so temperature applies and responses return faster.
            thinking = new { type = "disabled" },
            temperature = 0.2,
            max_tokens = 400,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content = JsonSerializer.Serialize(new
                    {
                        npcId = request.NpcId,
                        playerMessage = request.PlayerMessage,
                        day = request.Context.Day,
                        allowedTargets = request.Context.AllowedTargets,
                        allowedRewards = request.Context.AllowedRewards
                    })
                }
            }
        });
        return await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task<DeepSeekQuestPayload> ReadPayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        if (string.IsNullOrWhiteSpace(content))
            throw new DeepSeekUnavailableException("DeepSeek returned an empty message.");

        try
        {
            return JsonSerializer.Deserialize<DeepSeekQuestPayload>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new JsonException("Generated quest is null.");
        }
        catch (JsonException exception)
        {
            throw new DeepSeekUnavailableException("DeepSeek returned malformed quest JSON.", exception);
        }
    }
}
