using Echora.Api.Realtime;
using Echora.Api.Services;
using Hangfire;

namespace Echora.Api.Jobs;

/// <summary>重建一个自然日的 AI 摘要；输入未变化时不会调用模型。</summary>
public sealed class DayDigestJob(
    DayDigestService digests,
    DataUpdateNotifier notifier,
    ILogger<DayDigestJob> logger)
{
    /// <summary>重复执行安全；瞬时失败最多自动重试两次。</summary>
    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(long userId, DateOnly day, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        await digests.RebuildAsync(userId, day, cancellationToken);
        await notifier.NotifyAsync(userId, "settled", cancellationToken, "self");
        logger.LogInformation(
            "Day digest job completed: UserId {UserId}, Day {Day}, DurationMs {DurationMs}",
            userId,
            day,
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
    }
}
