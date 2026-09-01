using Echora.Api.Entities;
using Echora.Api.Services;
using Echora.Api.Workflows;
using Echora.Api.Realtime;
using Hangfire;
using SqlSugar;

namespace Echora.Api.Jobs;

/// <summary>恢复并执行一条消息或一刻尚未成功的三个分析分支。</summary>
public sealed class AnalysisJob(
    ISqlSugarClient db,
    AnalysisService analysis,
    AnalysisWorkflow workflow,
    IBackgroundJobClient jobs,
    DataUpdateNotifier notifier,
    ILogger<AnalysisJob> logger)
{
    /// <summary>瞬时失败最多自动重试两次；成功分支不会重复调用。</summary>
    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(long analysisRunId, CancellationToken cancellationToken)
    {
        var run = await db.Queryable<AnalysisRun>().Where(item => item.Id == analysisRunId).FirstAsync(cancellationToken);
        if (run is null) return;
        var branches = PendingBranches(run);
        if (branches.Length == 0) return;
        foreach (var branch in branches) SetStatus(run, branch, "Running", null, null, true);
        run.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(run).ExecuteCommandAsync(cancellationToken);
        await notifier.NotifyAsync(run.UserId, "running", cancellationToken, "analysis");
        logger.LogInformation(
            "Analysis Job started: AnalysisRunId {AnalysisRunId}, TargetMessageId {TargetMessageId}, MomentId {MomentId}, Branches {Branches}",
            run.Id,
            run.TargetMessageId,
            run.MomentId,
            branches);
        try
        {
            var input = await analysis.BuildInputAsync(run, branches, cancellationToken);
            if (input is null)
            {
                foreach (var branch in branches) SetStatus(run, branch, "Failed", "分析来源不存在。", null, false);
                await db.Updateable(run).ExecuteCommandAsync(cancellationToken);
                await notifier.NotifyAsync(run.UserId, "settled", cancellationToken, "analysis", "archive", "self");
                return;
            }
            var result = await workflow.RunAsync(input, cancellationToken);
            foreach (var branch in result.Branches)
            {
                try
                {
                    if (!branch.Succeeded) throw new InvalidOperationException(branch.Error ?? "后台分析失败。");
                    await analysis.SaveBranchAsync(run, branch, input, cancellationToken);
                    SetStatus(run, branch.Branch, "Succeeded", null, branch.DurationMilliseconds, false);
                    if (branch.Branch == "Emotion")
                    {
                        var day = DateOnly.FromDateTime(input.OccurredAt.LocalDateTime);
                        jobs.Enqueue<EmotionSummaryJob>(job =>
                            job.ExecuteAsync(run.UserId, day, CancellationToken.None));
                        try
                        {
                            // 延迟入队把一段连续对话合并成一次摘要生成；入队失败不影响分析结果。
                            jobs.Schedule<DayDigestJob>(
                                job => job.ExecuteAsync(run.UserId, day, CancellationToken.None),
                                TimeSpan.FromMinutes(2));
                        }
                        catch (Exception exception)
                        {
                            logger.LogWarning(exception, "Day digest enqueue failed: UserId {UserId}, Day {Day}", run.UserId, day);
                        }
                    }
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Analysis branch failed: AnalysisRunId {AnalysisRunId}, Branch {Branch}", run.Id, branch.Branch);
                    SetStatus(run, branch.Branch, "Failed", branch.Error ?? "分析结果保存失败。", branch.DurationMilliseconds, false);
                }
                run.UpdatedAt = DateTimeOffset.UtcNow;
                await db.Updateable(run).ExecuteCommandAsync(cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Analysis Workflow failed: AnalysisRunId {AnalysisRunId}", run.Id);
            foreach (var branch in branches.Where(branch => Status(run, branch) == "Running"))
                SetStatus(run, branch, "Failed", "后台分析执行失败。", null, false);
            run.UpdatedAt = DateTimeOffset.UtcNow;
            await db.Updateable(run).ExecuteCommandAsync(CancellationToken.None);
            await notifier.NotifyAsync(run.UserId, "settled", CancellationToken.None, "analysis", "archive", "self");
            throw;
        }
        await notifier.NotifyAsync(run.UserId, "settled", cancellationToken, "analysis", "archive", "self");
        try
        {
            // 新记录需要补向量；入队失败不影响分析结果，由巡检 backfill 兜底。
            jobs.Enqueue<MemoryEmbeddingJob>(job => job.ExecuteAsync(run.UserId, CancellationToken.None));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Memory embedding enqueue failed: UserId {UserId}", run.UserId);
        }
        var failures = branches.Where(branch => Status(run, branch) == "Failed").ToArray();
        logger.LogInformation("Analysis Job completed: AnalysisRunId {AnalysisRunId}, FailedBranches {FailedBranches}", run.Id, failures);
        if (failures.Length > 0) throw new InvalidOperationException($"后台分析分支失败：{string.Join(',', failures)}。");
    }

    /// <summary>选择未成功的分支。</summary>
    private static string[] PendingBranches(AnalysisRun run) =>
        new[] { "LifeRecord", "Recognition", "Emotion" }
            .Where(branch => Status(run, branch) is "Pending" or "Running" or "Failed")
            .ToArray();

    /// <summary>读取分支状态。</summary>
    private static string Status(AnalysisRun run, string branch) => branch switch
    {
        "LifeRecord" => run.LifeRecordStatus,
        "Recognition" => run.RecognitionStatus,
        "Emotion" => run.EmotionStatus,
        _ => throw new ArgumentOutOfRangeException(nameof(branch)),
    };

    /// <summary>修改分支状态、错误、耗时和尝试次数。</summary>
    private static void SetStatus(AnalysisRun run, string branch, string status, string? error, long? duration, bool incrementAttempt)
    {
        switch (branch)
        {
            case "LifeRecord":
                run.LifeRecordStatus = status; run.LifeRecordError = error; run.LifeRecordDurationMs = duration;
                if (incrementAttempt) run.LifeRecordAttempts++;
                break;
            case "Recognition":
                run.RecognitionStatus = status; run.RecognitionError = error; run.RecognitionDurationMs = duration;
                if (incrementAttempt) run.RecognitionAttempts++;
                break;
            case "Emotion":
                run.EmotionStatus = status; run.EmotionError = error; run.EmotionDurationMs = duration;
                if (incrementAttempt) run.EmotionAttempts++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(branch));
        }
    }
}
