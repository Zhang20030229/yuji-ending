using Echora.Api.Authentication;
using Echora.Api.Entities;
using Echora.Api.Jobs;
using Hangfire;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>把一刻、日常分析和心迹报告转换成设置页可查看和重试的安全运行日志。</summary>
public sealed class RunLogService(
    ISqlSugarClient db,
    IBackgroundJobClient jobs,
    IHttpContextAccessor accessor)
{
    private long UserId => accessor.HttpContext?.User.GetRequiredId()
        ?? throw new UnauthorizedAccessException("当前请求没有用户身份。");

    /// <summary>读取拾光和遇己当前仍在等待或运行的分支数量。</summary>
    public async Task<AnalysisStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var runs = await db.Queryable<AnalysisRun>()
            .Where(item => item.UserId == UserId
                && (item.LifeRecordStatus == "Pending" || item.LifeRecordStatus == "Running"
                    || item.RecognitionStatus == "Pending" || item.RecognitionStatus == "Running"
                    || item.EmotionStatus == "Pending" || item.EmotionStatus == "Running"))
            .ToListAsync(cancellationToken);
        var momentCount = await db.Queryable<Moment>()
            .CountAsync(item => item.UserId == UserId && (item.Status == "Pending" || item.Status == "Running"), cancellationToken);
        return new AnalysisStatus(
            momentCount + runs.Count(item => item.LifeRecordStatus is "Pending" or "Running"),
            runs.Count(item => item.RecognitionStatus is "Pending" or "Running"),
            runs.Count(item => item.EmotionStatus is "Pending" or "Running"));
    }

    /// <summary>每个失败分支返回一条日志，不包含聊天或模型正文。</summary>
    public async Task<IReadOnlyList<RunLogItem>> GetAsync(CancellationToken cancellationToken)
    {
        var moments = await db.Queryable<Moment>()
            .Where(item => item.UserId == UserId && item.Status == "Failed")
            .OrderBy(item => item.UpdatedAt, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        var runs = await db.Queryable<AnalysisRun>()
            .Where(item => item.UserId == UserId
                && (item.LifeRecordStatus == "Failed" || item.RecognitionStatus == "Failed" || item.EmotionStatus == "Failed"))
            .OrderBy(item => item.UpdatedAt, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        var reportPacks = await db.Queryable<ReportPack>()
            .Where(item => item.UserId == UserId
                && (item.Status == "Partial" || item.Status == "Failed"))
            .OrderBy(item => item.UpdatedAt, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        var reportPackIds = reportPacks.Select(item => item.Id).ToArray();
        var reportSections = reportPackIds.Length == 0
            ? []
            : await db.Queryable<ReportSection>()
                .Where(item => item.UserId == UserId
                    && reportPackIds.Contains(item.ReportPackId))
                .ToListAsync(cancellationToken);
        var result = new List<RunLogItem>();
        result.AddRange(moments.Select(moment => new RunLogItem(
            moment.Id.ToString(),
            "moment",
            "Moment",
            "一刻整理",
            moment.Status,
            moment.ErrorMessage ?? "一刻整理失败。",
            moment.UpdatedAt,
            moment.AttemptCount,
            moment.DurationMs,
            null,
            true)));
        foreach (var run in runs)
        {
            Add(result, run, "LifeRecord", "生活记录整理", run.LifeRecordStatus, run.LifeRecordError, run.LifeRecordAttempts, run.LifeRecordDurationMs);
            Add(result, run, "Recognition", "认识整理", run.RecognitionStatus, run.RecognitionError, run.RecognitionAttempts, run.RecognitionDurationMs);
            Add(result, run, "Emotion", "情绪整理", run.EmotionStatus, run.EmotionError, run.EmotionAttempts, run.EmotionDurationMs);
        }
        foreach (var section in reportSections.Where(item => item.Status == "Failed"))
            result.Add(new RunLogItem(
                section.ReportPackId.ToString(),
                "report",
                section.Kind,
                $"{SectionLabel(section.Kind)}生成",
                section.Status,
                section.ErrorSummary ?? "报告分区生成失败。",
                section.UpdatedAt,
                section.AttemptCount,
                section.DurationMs,
                null,
                true));
        foreach (var pack in reportPacks.Where(item =>
                     item.OverallContentJson is null
                     && !string.IsNullOrWhiteSpace(item.ErrorSummary)
                     && reportSections.Count(section =>
                         section.ReportPackId == item.Id
                         && section.Status == "Complete"
                         && section.ContentJson is not null) >= 2))
            result.Add(new RunLogItem(
                pack.Id.ToString(),
                "report",
                "Overall",
                "综合心迹生成",
                "Failed",
                pack.ErrorSummary!,
                pack.UpdatedAt,
                pack.AttemptCount,
                pack.DurationMs,
                null,
                true));
        return result;
    }

    /// <summary>把当前版本所有失败分支恢复为 Pending 并重新入队。</summary>
    public async Task<bool> RetryAsync(long runId, CancellationToken cancellationToken)
    {
        var run = await db.Queryable<AnalysisRun>().Where(item => item.Id == runId && item.UserId == UserId).FirstAsync(cancellationToken);
        if (run is null) return false;
        if (run.LifeRecordStatus == "Failed") { run.LifeRecordStatus = "Pending"; run.LifeRecordError = null; }
        if (run.RecognitionStatus == "Failed") { run.RecognitionStatus = "Pending"; run.RecognitionError = null; }
        if (run.EmotionStatus == "Failed") { run.EmotionStatus = "Pending"; run.EmotionError = null; }
        run.HangfireJobId = jobs.Enqueue<AnalysisJob>(job => job.ExecuteAsync(run.Id, CancellationToken.None));
        run.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(run).ExecuteCommandAsync(cancellationToken);
        return true;
    }

    /// <summary>只把失败分支加入返回列表。</summary>
    private static void Add(
        ICollection<RunLogItem> result,
        AnalysisRun run,
        string branch,
        string stage,
        string status,
        string? error,
        int attempts,
        long? duration)
    {
        if (status != "Failed") return;
        result.Add(new RunLogItem(
            run.Id.ToString(),
            "processing_run",
            branch,
            stage,
            status,
            error ?? "后台整理失败。",
            run.UpdatedAt,
            attempts,
            duration,
            run.ConversationId?.ToString(),
            true));
    }

    /// <summary>设置页使用的一条分支日志。</summary>
    public sealed record RunLogItem(string Id, string Kind, string Branch, string Stage, string State, string Summary, DateTimeOffset OccurredAt, int AttemptCount, long? DurationMilliseconds, string? ConversationSessionId, bool CanRetry)
    {
        /// <summary>当前错误只提供安全摘要，不再建立冗余错误码体系。</summary>
        public string? ErrorCode => null;
    }

    /// <summary>前端顶部整理提示所需的最小状态。</summary>
    public sealed record AnalysisStatus(int LifeRecordCount, int RecognitionCount, int EmotionCount);

    /// <summary>报告分区的用户可见名称。</summary>
    private static string SectionLabel(string kind) => kind switch
    {
        "Life" => "生活回望",
        "Emotion" => "情绪脉络",
        "Relationship" => "人际往来",
        "Recognition" => "自我认识",
        _ => "报告",
    };
}
