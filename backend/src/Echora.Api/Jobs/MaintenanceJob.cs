using Echora.Api.Entities;
using Echora.Api.Services;
using Hangfire;
using SqlSugar;

namespace Echora.Api.Jobs;

/// <summary>补投遗漏分析并补算情绪投影。</summary>
public sealed class MaintenanceJob(
    ISqlSugarClient db,
    IBackgroundJobClient jobs,
    EmotionSummaryService summaries,
    MemoryEmbeddingService embeddings,
    ILogger<MaintenanceJob> logger)
{
    /// <summary>仅补投尚未进入队列的分析，供五分钟轻量巡检使用。</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task RecoverAnalysisAsync(CancellationToken cancellationToken)
    {
        var recovered = await RecoverAnalysisRunsAsync(cancellationToken);
        logger.LogInformation(
            "Analysis recovery completed: RecoveredAnalysisCount {RecoveredAnalysisCount}",
            recovered);
    }

    /// <summary>周期任务由下一轮自然补偿，不叠加自动重试。</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var recovered = await RecoverAnalysisRunsAsync(cancellationToken);
        await summaries.RepairMissingAsync(cancellationToken);
        var orphans = await embeddings.RemoveOrphansAsync(cancellationToken);
        logger.LogInformation(
            "Daily maintenance completed: RecoveredAnalysisCount {RecoveredAnalysisCount}, EmbeddingOrphansRemoved {EmbeddingOrphansRemoved}",
            recovered,
            orphans);
    }

    /// <summary>补投没有 Hangfire 作业编号的待处理分析并返回数量。</summary>
    private async Task<int> RecoverAnalysisRunsAsync(CancellationToken cancellationToken)
    {
        var runs = await db.Queryable<AnalysisRun>()
            .Where(item => item.HangfireJobId == null
                && (item.LifeRecordStatus == "Pending" || item.RecognitionStatus == "Pending" || item.EmotionStatus == "Pending"))
            .ToListAsync(cancellationToken);
        foreach (var run in runs)
        {
            run.HangfireJobId = jobs.Enqueue<AnalysisJob>(job => job.ExecuteAsync(run.Id, CancellationToken.None));
            run.UpdatedAt = DateTimeOffset.UtcNow;
            await db.Updateable(run).ExecuteCommandAsync(cancellationToken);
        }
        return runs.Count;
    }
}
