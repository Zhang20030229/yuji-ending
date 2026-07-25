using Echora.Api.Agents;
using Echora.Api.Entities;
using Echora.Api.Services;
using Echora.Api.Workflows;
using Echora.Api.Realtime;
using Hangfire;
using SqlSugar;

namespace Echora.Api.Jobs;

/// <summary>构建并保存一份心迹报告，或只重试其中一个失败部分。</summary>
public sealed class HeartReportJob(
    ISqlSugarClient db,
    HeartReportContextService contexts,
    HeartReportWorkflow workflow,
    LifeReportAgent life,
    EmotionReportAgent emotion,
    RelationshipReportAgent relationship,
    RecognitionReportAgent recognition,
    ReportComposerAgent composer,
    DataUpdateNotifier notifier,
    ILogger<HeartReportJob> logger)
{
    /// <summary>完整报告遇到工作流级瞬时错误时最多自动重试两次。</summary>
    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(
        long reportPackId,
        CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var pack = await db.Queryable<ReportPack>()
            .Where(item => item.Id == reportPackId)
            .FirstAsync(cancellationToken);
        if (pack is null) return;
        pack.Status = "Running";
        pack.ErrorSummary = null;
        pack.AttemptCount++;
        pack.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(pack).ExecuteCommandAsync(cancellationToken);
        await notifier.NotifyAsync(pack.UserId, "running", cancellationToken, "reports", "analysis");
        logger.LogInformation(
            "Heart report Job started: ReportPackId {ReportPackId}, UserId {UserId}, Attempt {Attempt}, StartDate {StartDate}, EndDate {EndDate}",
            pack.Id,
            pack.UserId,
            pack.AttemptCount,
            pack.StartDate,
            pack.EndDate);
        try
        {
            var input = await contexts.BuildAsync(pack, cancellationToken)
                ?? throw new InvalidOperationException("报告用户或来源已经不存在。");
            var rows = await LoadSectionsAsync(pack, cancellationToken);
            await PrepareSectionsAsync(rows, input, cancellationToken);
            var eligibleCount = input.Sections.Count(item => item.Eligible);
            if (eligibleCount == 0)
            {
                pack.Status = "NotEnoughData";
                pack.OverallContentJson = null;
                pack.DurationMs = Elapsed(startedAt);
                pack.UpdatedAt = DateTimeOffset.UtcNow;
                await db.Updateable(pack).ExecuteCommandAsync(cancellationToken);
                logger.LogInformation(
                    "Heart report completed without model calls: ReportPackId {ReportPackId}, Status {Status}",
                    pack.Id,
                    pack.Status);
                return;
            }

            var result = await workflow.RunAsync(input, cancellationToken);
            foreach (var branch in result.Sections)
            {
                var row = rows.Single(item => item.Kind == branch.Kind);
                SaveBranch(row, branch);
                await db.Updateable(row).ExecuteCommandAsync(cancellationToken);
            }
            if (result.Overall is not null)
                SaveOverall(pack, result.Overall);
            else
            {
                pack.OverallContentJson = null;
                pack.ErrorSummary = null;
            }
            pack.Status = DetermineStatus(rows, result.Overall);
            pack.DurationMs = Elapsed(startedAt);
            pack.UpdatedAt = DateTimeOffset.UtcNow;
            await db.Updateable(pack).ExecuteCommandAsync(cancellationToken);
            logger.LogInformation(
                "Heart report Job completed: ReportPackId {ReportPackId}, Status {Status}, CompletedSections {CompletedSections}, FailedSections {FailedSections}, DurationMs {DurationMs}",
                pack.Id,
                pack.Status,
                rows.Count(item => item.Status == "Complete"),
                rows.Count(item => item.Status == "Failed"),
                pack.DurationMs);
        }
        catch (Exception exception)
        {
            // ponytail: MVP 只保存统一安全错误；若以后需要精确恢复工作流节点，再引入持久检查点。
            var interrupted = await db.Queryable<ReportSection>()
                .Where(item => item.ReportPackId == pack.Id
                    && item.UserId == pack.UserId
                    && item.Status == "Running")
                .ToListAsync(CancellationToken.None);
            foreach (var section in interrupted)
            {
                section.Status = "Failed";
                section.ErrorSummary = "心迹报告执行失败。";
                section.UpdatedAt = DateTimeOffset.UtcNow;
                await db.Updateable(section).ExecuteCommandAsync(CancellationToken.None);
            }
            pack.Status = "Failed";
            pack.ErrorSummary = "心迹报告执行失败。";
            pack.DurationMs = Elapsed(startedAt);
            pack.UpdatedAt = DateTimeOffset.UtcNow;
            await db.Updateable(pack).ExecuteCommandAsync(CancellationToken.None);
            logger.LogError(
                exception,
                "Heart report Job failed: ReportPackId {ReportPackId}, DurationMs {DurationMs}",
                pack.Id,
                pack.DurationMs);
            throw;
        }
        finally
        {
            await notifier.NotifyAsync(pack.UserId, "settled", CancellationToken.None, "reports", "analysis");
        }
    }

    /// <summary>只重试一个失败子报告；成功分区不会再次调用模型。</summary>
    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task RetrySectionAsync(
        long reportPackId,
        string kind,
        CancellationToken cancellationToken)
    {
        var pack = await db.Queryable<ReportPack>()
            .Where(item => item.Id == reportPackId)
            .FirstAsync(cancellationToken);
        if (pack is null) return;
        var input = await contexts.BuildAsync(pack, cancellationToken)
            ?? throw new InvalidOperationException("报告用户或来源已经不存在。");
        var sectionInput = input.Sections.SingleOrDefault(item => item.Kind == kind)
            ?? throw new InvalidDataException("报告分区不存在。");
        var row = await db.Queryable<ReportSection>()
            .Where(item => item.ReportPackId == pack.Id
                && item.UserId == pack.UserId
                && item.Kind == kind)
            .FirstAsync(cancellationToken)
            ?? throw new InvalidOperationException("报告分区已经删除。");
        row.MetricsJson = sectionInput.MetricsJson;
        row.EvidenceJson = sectionInput.EvidenceJson;
        row.ContentJson = null;
        row.ErrorSummary = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        if (!sectionInput.Eligible)
        {
            row.Status = "NotEnoughData";
            await db.Updateable(row).ExecuteCommandAsync(cancellationToken);
            await RefreshPackAsync(pack, input, cancellationToken, forceComposer: true);
            await notifier.NotifyAsync(pack.UserId, "settled", cancellationToken, "reports", "analysis");
            return;
        }

        row.Status = "Running";
        row.AttemptCount++;
        await db.Updateable(row).ExecuteCommandAsync(cancellationToken);
        await notifier.NotifyAsync(pack.UserId, "running", cancellationToken, "reports", "analysis");
        var result = await RunSingleAsync(kind, input, cancellationToken);
        SaveBranch(row, result);
        await db.Updateable(row).ExecuteCommandAsync(cancellationToken);
        await RefreshPackAsync(pack, input, cancellationToken, forceComposer: true);
        await notifier.NotifyAsync(pack.UserId, "settled", cancellationToken, "reports", "analysis");
    }

    /// <summary>只重试综合心迹，不重新运行四个子报告。</summary>
    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task RetryOverallAsync(
        long reportPackId,
        CancellationToken cancellationToken)
    {
        var pack = await db.Queryable<ReportPack>()
            .Where(item => item.Id == reportPackId)
            .FirstAsync(cancellationToken);
        if (pack is null) return;
        var input = await contexts.BuildAsync(pack, cancellationToken)
            ?? throw new InvalidOperationException("报告用户或来源已经不存在。");
        await RefreshPackAsync(pack, input, cancellationToken, forceComposer: true);
        await notifier.NotifyAsync(pack.UserId, "settled", cancellationToken, "reports", "analysis");
    }

    /// <summary>把门槛、指标和证据先持久化，刷新页面即可看到真实运行状态。</summary>
    private async Task PrepareSectionsAsync(
        IReadOnlyList<ReportSection> rows,
        HeartReportInput input,
        CancellationToken cancellationToken)
    {
        foreach (var section in input.Sections)
        {
            var row = rows.Single(item => item.Kind == section.Kind);
            row.Status = section.Eligible ? "Running" : "NotEnoughData";
            row.MetricsJson = section.MetricsJson;
            row.EvidenceJson = section.EvidenceJson;
            row.ContentJson = null;
            row.ErrorSummary = null;
            row.DurationMs = null;
            if (section.Eligible) row.AttemptCount++;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.Updateable(row).ExecuteCommandAsync(cancellationToken);
        }
    }

    /// <summary>保存一个分支结果，不影响其他分区。</summary>
    private static void SaveBranch(ReportSection row, ReportBranchResult result)
    {
        row.Status = result.Succeeded ? "Complete" : "Failed";
        row.ContentJson = result.Succeeded ? result.ContentJson : null;
        row.ErrorSummary = result.Succeeded ? null : result.Error ?? "报告分区生成失败。";
        row.DurationMs = result.DurationMilliseconds;
        row.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>保存综合结果。</summary>
    private static void SaveOverall(ReportPack pack, ReportBranchResult result)
    {
        pack.OverallContentJson = result.Succeeded ? result.ContentJson : null;
        pack.ErrorSummary = result.Succeeded ? null : result.Error ?? "综合心迹生成失败。";
    }

    /// <summary>重试后根据当前四个分区重新生成综合内容和最终状态。</summary>
    private async Task RefreshPackAsync(
        ReportPack pack,
        HeartReportInput input,
        CancellationToken cancellationToken,
        bool forceComposer = false)
    {
        var rows = await LoadSectionsAsync(pack, cancellationToken);
        var completed = rows.Where(item => item.Status == "Complete" && item.ContentJson is not null)
            .Select(item => new ReportBranchResult(item.Kind, true, item.ContentJson, null, item.DurationMs ?? 0))
            .ToArray();
        ReportBranchResult? overall = null;
        if (completed.Length >= 2 && (forceComposer || rows.Any(item => item.Status == "Failed") || pack.OverallContentJson is null))
        {
            overall = await composer.RunAsync(input, completed, cancellationToken);
            SaveOverall(pack, overall);
        }
        else if (completed.Length < 2)
        {
            pack.OverallContentJson = null;
            pack.ErrorSummary = null;
        }
        pack.Status = DetermineStatus(rows, overall);
        pack.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(pack).ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>按名称运行一个报告子智能体。</summary>
    private ValueTask<ReportBranchResult> RunSingleAsync(
        string kind,
        HeartReportInput input,
        CancellationToken cancellationToken) =>
        kind switch
        {
            "Life" => life.RunAsync(input, cancellationToken),
            "Emotion" => emotion.RunAsync(input, cancellationToken),
            "Relationship" => relationship.RunAsync(input, cancellationToken),
            "Recognition" => recognition.RunAsync(input, cancellationToken),
            _ => throw new InvalidDataException("未知报告分区。"),
        };

    /// <summary>读取报告包固定四个分区。</summary>
    private async Task<List<ReportSection>> LoadSectionsAsync(
        ReportPack pack,
        CancellationToken cancellationToken)
    {
        var rows = await db.Queryable<ReportSection>()
            .Where(item => item.ReportPackId == pack.Id && item.UserId == pack.UserId)
            .ToListAsync(cancellationToken);
        if (rows.Count != 4) throw new InvalidDataException("报告包没有四个固定分区。");
        return rows;
    }

    /// <summary>根据真实分区和综合结果决定报告包状态。</summary>
    private static string DetermineStatus(
        IReadOnlyList<ReportSection> sections,
        ReportBranchResult? overall)
    {
        var completed = sections.Count(item => item.Status == "Complete");
        var failed = sections.Count(item => item.Status == "Failed");
        var overallFailed = overall is { Succeeded: false };
        if (completed == 0 && failed == 0) return "NotEnoughData";
        if (completed == 0) return "Failed";
        if (failed > 0 || overallFailed) return "Partial";
        return "Complete";
    }

    private static long Elapsed(long startedAt) =>
        (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
}
