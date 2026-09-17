using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Haven.Gateway.Contracts;
using Haven.Gateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Haven.Gateway.Tests;

public sealed class CampQuestTests
{
    private readonly CampQuestPolicy _policy = new();

    private CampQuestRequest Request() => new("camp-request", 1, "easy", "今天想采石头",
        new CampQuestWorld("save-session", 1, 8, false, 0, 0, 0, 0, 1, true), [],
        [_policy.Catalog.Quests.Single(q => q.Id == "fire_preparation_small")]);

    [Fact]
    public void RealCatalogCandidatePasses() => Assert.Null(_policy.Validate(Request()));

    [Theory]
    [InlineData("reward")]
    [InlineData("quantity")]
    [InlineData("id")]
    [InlineData("completed")]
    [InlineData("duplicate")]
    [InlineData("large")]
    [InlineData("world")]
    public void InvalidInputsAreRejectedBeforeApiCall(string scenario)
    {
        var request = Request();
        var quest = request.Candidates[0];
        request = scenario switch
        {
            "reward" => request with { Candidates = [quest with { RewardQuantity = 999 }] },
            "quantity" => request with { Candidates = [quest with { Quantity = -1 }] },
            "id" => request with { Candidates = [quest with { ItemId = "gold" }] },
            "completed" => request with { CompletedItems = ["rock"] },
            "duplicate" => request with { Candidates = [quest, quest] },
            "large" => request with { Candidates = [_policy.Catalog.Quests.Single(q => q.Id == "fire_preparation_large")] },
            "world" => request with { Context = request.Context with { Hour = float.NaN } },
            _ => request
        };
        Assert.NotNull(_policy.Validate(request));
    }

    [Fact]
    public void ContextEligibilityIsValidated()
    {
        var request = Request();
        Assert.NotNull(_policy.Validate(request with { Context = request.Context with { HasFirepit = true } }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"candidateId\":\"gold\",\"dialogue\":\"奖励金币\"}")]
    [InlineData("{\"candidateId\":\"fire_preparation_small\",\"dialogue\":\"\"}")]
    [InlineData("{\"candidateId\":\"fire_preparation_small\",\"dialogue\":\"<b>木材</b>\"}")]
    [InlineData("{\"candidateId\":\"fire_preparation_small\",\"dialogue\":\"石料\",\"rewardQuantity\":999}")]
    [InlineData("[]")]
    public void UnsafeOrEmptyModelPayloadIsRejected(string content)
    {
        Assert.Throws<InvalidDataException>(() => CampQuestPolicy.Parse(content, Request()));
    }

    [Fact]
    public async Task ValidResponseUsesNonThinkingJsonAndEightSecondBoundedCall()
    {
        var handler = new Handler { Body = Envelope("{\"candidateId\":\"fire_preparation_small\",\"dialogue\":\"先为营地储备些石料吧。\"}") };
        var result = await Service(handler).GenerateAsync(Request(), CancellationToken.None);
        Assert.Equal("camp-request", result.RequestId);
        Assert.Equal("deepseek", result.Source);
        Assert.Equal(1, handler.Calls);
        using var sent = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("disabled", sent.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("json_object", sent.RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[null]}")]
    [InlineData("[]")]
    [InlineData("malformed")]
    public async Task MissingOrMalformedEnvelopesAreRecoverable(string body)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(new Handler { Body = body }).GenerateAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task EmptyJsonContentIsRecoverable()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(new Handler { Body = Envelope("") }).GenerateAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task NoKeyDoesNotSendAnyRequest()
    {
        var handler = new Handler();
        await Assert.ThrowsAsync<DeepSeekUnavailableException>(() => Service(handler, "").GenerateAsync(Request(), CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task TimeoutOrHttpFailureDoesNotRetry()
    {
        var handler = new Handler { Cancel = true };
        await Assert.ThrowsAsync<DeepSeekUnavailableException>(() => Service(handler).GenerateAsync(Request(), CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        handler = new Handler { Status = HttpStatusCode.TooManyRequests };
        await Assert.ThrowsAsync<DeepSeekUnavailableException>(() => Service(handler).GenerateAsync(Request(), CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task UnresponsiveApiIsActuallyCancelledByEightSecondBudget()
    {
        var handler = new Handler { Stall = true };
        var elapsed = Stopwatch.StartNew();
        await Assert.ThrowsAsync<DeepSeekUnavailableException>(() => Service(handler).GenerateAsync(Request(), CancellationToken.None));
        Assert.InRange(elapsed.Elapsed.TotalSeconds, 7.5, 12);
        Assert.Equal(1, handler.Calls);
    }

    private static string Envelope(string content) => JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });
    private static CampQuestGenerationService Service(Handler handler, string key = "unit-test-not-a-real-key") =>
        new(new HttpClient(handler), Options.Create(new DeepSeekOptions { ApiKey = key }), NullLogger<CampQuestGenerationService>.Instance);

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public string? RequestBody;
        public string Body = "{}";
        public bool Cancel;
        public bool Stall;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (Cancel) throw new TaskCanceledException("Simulated API deadline.");
            if (Stall) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
}
