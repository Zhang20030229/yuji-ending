using Echora.Api.Services;
using Echora.Api.Realtime;
using Hangfire;

namespace Echora.Api.Jobs;

/// <summary>重建一个自然日以及所属月份的确定性情绪投影。</summary>
public sealed class EmotionSummaryJob(
    EmotionSummaryService summaries,
    DataUpdateNotifier notifier,
    ILogger<EmotionSummaryJob> logger)
{
    /// <summary>重复执行结果一致；瞬时失败最多自动重试两次。</summary>
    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(long userId, DateOnly day, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        await summaries.RebuildAsync(userId, day, cancellationToken);
        await notifier.NotifyAsync(userId, "settled", cancellationToken, "self");
        logger.LogInformation(
            "Emotion summary rebuilt: UserId {UserId}, Day {Day}, DurationMs {DurationMs}",
            userId,
            day,
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
    }
}
