using System.Diagnostics;
using Haven.Gateway.Contracts;

namespace Haven.Gateway.Services;

public sealed class QuestGenerationService
{
    private readonly IDeepSeekClient _deepSeek;
    private readonly ILogger<QuestGenerationService> _logger;

    public QuestGenerationService(IDeepSeekClient deepSeek, ILogger<QuestGenerationService> logger)
    {
        _deepSeek = deepSeek;
        _logger = logger;
    }

    public async Task<QuestGenerateResponse> GenerateAsync(QuestGenerateRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var generated = await _deepSeek.GenerateQuestAsync(request, cancellationToken);
        var validationError = QuestPolicy.ValidateGenerated(generated, request);
        if (validationError is not null)
            throw new InvalidDataException(validationError);

        _logger.LogInformation("Generated and validated quest in {ElapsedMs} ms. requestId={RequestId}", stopwatch.ElapsedMilliseconds, request.RequestId);
        return new QuestGenerateResponse(
            request.RequestId,
            generated.Dialogue.Trim(),
            generated.QuestType,
            generated.TargetId,
            generated.Count,
            generated.RewardId,
            "deepseek");
    }
}
