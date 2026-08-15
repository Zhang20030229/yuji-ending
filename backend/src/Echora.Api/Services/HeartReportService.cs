using System.Text.Json;
using Echora.Api.Agents;
using Echora.Api.Authentication;
using Echora.Api.Contracts;
using Echora.Api.Entities;
using Echora.Api.Jobs;
using Echora.Api.Workflows;
using Hangfire;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>创建、查询和重试心迹报告，并确定性保存 WHO-5 自评。</summary>
public sealed class HeartReportService(
    ISqlSugarClient db,
    IBackgroundJobClient jobs,
    HeartReportContextService contexts,
    IHttpContextAccessor accessor,
    ILogger<HeartReportService> logger)
{
    private long UserId => accessor.HttpContext?.User.GetRequiredId()
        ?? throw new UnauthorizedAccessException("当前请求没有用户身份。");

    /// <summary>读取当前用户的报告历史。</summary>
    public async Task<IReadOnlyList<ReportListItem>> GetReportsAsync(
        CancellationToken cancellationToken)
    {
        var packs = await db.Queryable<ReportPack>()
            .Where(item => item.UserId == UserId)
            .OrderBy(item => item.UpdatedAt, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        if (packs.Count == 0) return [];
        var packIds = packs.Select(item => item.Id).ToArray();
        var sections = await db.Queryable<ReportSection>()
            .Where(item => item.UserId == UserId && packIds.Contains(item.ReportPackId))
            .ToListAsync(cancellationToken);
        return packs.Select(pack =>
        {
            var content = DeserializeContent(pack.OverallContentJson);
            var current = sections.Where(item => item.ReportPackId == pack.Id).ToArray();
            content ??= current.Select(item => DeserializeContent(item.ContentJson)).FirstOrDefault(item => item is not null);
            return new ReportListItem(
                pack.Id,
                pack.PeriodType,
                pack.StartDate,
                pack.EndDate,
                pack.Status,
                content?.Headline,
                current.Count(item => item.Status == "Complete"),
                pack.CreatedAt,
                pack.UpdatedAt);
        }).ToArray();
    }

    /// <summary>读取一个报告包及其四个分区和依据。</summary>
    public async Task<ReportDetail?> GetReportAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var pack = await db.Queryable<ReportPack>()
            .Where(item => item.Id == id && item.UserId == UserId)
            .FirstAsync(cancellationToken);
        if (pack is null) return null;
        var rows = await db.Queryable<ReportSection>()
            .Where(item => item.ReportPackId == id && item.UserId == UserId)
            .ToListAsync(cancellationToken);
        var sections = rows
            .OrderBy(item => Array.IndexOf(SectionOrder, item.Kind))
            .Select(ToSection)
            .ToArray();
        var overallEvidence = sections
            .SelectMany(item => item.Evidence)
            .GroupBy(item => item.Ref, StringComparer.Ordinal)
            .Select(group => group.First() with
            {
                ImageUrls = group.SelectMany(item => item.ImageUrls).Distinct().ToArray(),
                ImageDescriptions = group.SelectMany(item => item.ImageDescriptions).Distinct().ToArray(),
            })
            .OrderBy(item => item.OccurredAt)
            .ToArray();
        return new ReportDetail(
            pack.Id,
            pack.PeriodType,
            pack.StartDate,
            pack.EndDate,
            pack.TriggerType,
            pack.Status,
            DeserializeContent(pack.OverallContentJson),
            overallEvidence,
            sections,
            pack.ErrorSummary,
            pack.CreatedAt,
            pack.UpdatedAt);
    }

    /// <summary>创建或覆盖同一日期范围的手动报告，并只入队一次。</summary>
    public async Task<ReportListItem> GenerateAsync(
        GenerateReportRequest request,
        CancellationToken cancellationToken)
    {
        var range = ResolveRange(request);
        var pack = await CreateOrResetAsync(
            UserId,
            range.PeriodType,
            range.StartDate,
            range.EndDate,
            "Manual",
            cancellationToken);
        pack.HangfireJobId = jobs.Enqueue<HeartReportJob>(
            job => job.ExecuteAsync(pack.Id, CancellationToken.None));
        pack.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(pack).ExecuteCommandAsync(cancellationToken);
        logger.LogInformation(
            "Heart report enqueued: ReportPackId {ReportPackId}, UserId {UserId}, StartDate {StartDate}, EndDate {EndDate}",
            pack.Id,
            pack.UserId,
            pack.StartDate,
            pack.EndDate);
        return new ReportListItem(
            pack.Id,
            pack.PeriodType,
            pack.StartDate,
            pack.EndDate,
            pack.Status,
            null,
            0,
            pack.CreatedAt,
            pack.UpdatedAt);
    }

    /// <summary>自动周期只有至少两个分区满足门槛时才创建报告包。</summary>
    public async Task<bool> EnsureAutomaticAsync(
        long userId,
        string periodType,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var existing = await db.Queryable<ReportPack>()
            .Where(item => item.UserId == userId
                && item.StartDate == startDate
                && item.EndDate == endDate)
            .FirstAsync(cancellationToken);
        if (existing is not null)
        {
            // 自动报告失败后由下一次每日检查复用原报告包补偿，不生成第二张历史卡片。
            if (existing.TriggerType != "Automatic"
                || existing.Status is not ("Failed" or "Partial"))
                return false;
            var retried = await CreateOrResetAsync(
                userId,
                periodType,
                startDate,
                endDate,
                "Automatic",
                cancellationToken);
            retried.HangfireJobId = jobs.Enqueue<HeartReportJob>(
                job => job.ExecuteAsync(retried.Id, CancellationToken.None));
            retried.UpdatedAt = DateTimeOffset.UtcNow;
            await db.Updateable(retried).ExecuteCommandAsync(cancellationToken);
            return true;
        }
        var candidate = new ReportPack
        {
            UserId = userId,
            PeriodType = periodType,
            StartDate = startDate,
            EndDate = endDate,
            TriggerType = "Automatic",
        };
        var input = await contexts.BuildAsync(candidate, cancellationToken);
        if (input is null || input.Sections.Count(item => item.Eligible) < 2) return false;
        var pack = await CreateOrResetAsync(
            userId,
            periodType,
            startDate,
            endDate,
            "Automatic",
            cancellationToken);
        pack.HangfireJobId = jobs.Enqueue<HeartReportJob>(
            job => job.ExecuteAsync(pack.Id, CancellationToken.None));
        pack.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(pack).ExecuteCommandAsync(cancellationToken);
        return true;
    }

    /// <summary>只重试指定失败分区或综合心迹。</summary>
    public async Task<bool> RetryAsync(
        long id,
        string kind,
        CancellationToken cancellationToken)
    {
        var pack = await db.Queryable<ReportPack>()
            .Where(item => item.Id == id && item.UserId == UserId)
            .FirstAsync(cancellationToken);
        if (pack is null) return false;
        if (kind.Equals("Overall", StringComparison.OrdinalIgnoreCase))
        {
            if (pack.OverallContentJson is not null || string.IsNullOrWhiteSpace(pack.ErrorSummary))
                return false;
            var completedCount = await db.Queryable<ReportSection>()
                .Where(item => item.ReportPackId == pack.Id
                    && item.UserId == UserId
                    && item.Status == "Complete"
                    && item.ContentJson != null)
                .CountAsync(cancellationToken);
            // 日报告常常只有一个分区成立，因此综合心迹的最低分区数按周期分档。
            if (completedCount < ReportPeriod.MinimumComposableSections(pack.PeriodType))
                return false;
            pack.HangfireJobId = jobs.Enqueue<HeartReportJob>(
                job => job.RetryOverallAsync(pack.Id, CancellationToken.None));
        }
        else
        {
            var canonical = SectionOrder.FirstOrDefault(item => item.Equals(kind, StringComparison.OrdinalIgnoreCase));
            if (canonical is null) throw new ArgumentException("未知报告分区。");
            var section = await db.Queryable<ReportSection>()
                .Where(item => item.ReportPackId == pack.Id
                    && item.UserId == UserId
                    && item.Kind == canonical)
                .FirstAsync(cancellationToken);
            if (section?.Status != "Failed") return false;
            section.Status = "Pending";
            section.ErrorSummary = null;
            section.UpdatedAt = DateTimeOffset.UtcNow;
            await db.Updateable(section).ExecuteCommandAsync(cancellationToken);
            pack.HangfireJobId = jobs.Enqueue<HeartReportJob>(
                job => job.RetrySectionAsync(pack.Id, canonical, CancellationToken.None));
        }
        pack.Status = "Pending";
        pack.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(pack).ExecuteCommandAsync(cancellationToken);
        return true;
    }

    /// <summary>保存一次完整 WHO-5，并由固定公式计算分数。</summary>
    public async Task<WellbeingItem> SaveWellbeingAsync(
        WellbeingAssessmentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Answers is null || request.Answers.Length != 5)
            throw new ArgumentException("WHO-5 必须完成五道题。");
        if (request.Answers.Any(value => value is < 0 or > 5))
            throw new ArgumentException("每道题只能选择 0～5。");
        var raw = request.Answers.Sum();
        var item = new WellbeingAssessment
        {
            UserId = UserId,
            Answers = request.Answers.ToArray(),
            RawScore = raw,
            PercentageScore = raw * 4,
            AssessedAt = DateTimeOffset.UtcNow,
        };
        item.Id = await db.Insertable(item).ExecuteReturnBigIdentityAsync(cancellationToken);
        return ToWellbeing(item);
    }

    /// <summary>读取最近的 WHO-5 历史。</summary>
    public async Task<IReadOnlyList<WellbeingItem>> GetWellbeingAsync(
        CancellationToken cancellationToken)
    {
        var rows = await db.Queryable<WellbeingAssessment>()
            .Where(item => item.UserId == UserId)
            .OrderBy(item => item.AssessedAt, OrderByType.Desc)
            .Take(24)
            .ToListAsync(cancellationToken);
        return rows.Select(ToWellbeing).ToArray();
    }

    /// <summary>创建或重置一个幂等报告包及其四个固定分区。</summary>
    private async Task<ReportPack> CreateOrResetAsync(
        long userId,
        string periodType,
        DateOnly startDate,
        DateOnly endDate,
        string triggerType,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var pack = await db.Queryable<ReportPack>()
            .Where(item => item.UserId == userId
                && item.StartDate == startDate
                && item.EndDate == endDate)
            .FirstAsync(cancellationToken);
        db.Ado.BeginTran();
        try
        {
            if (pack is null)
            {
                pack = new ReportPack
                {
                    UserId = userId,
                    PeriodType = periodType,
                    StartDate = startDate,
                    EndDate = endDate,
                    TriggerType = triggerType,
                    Status = "Pending",
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                pack.Id = await db.Insertable(pack).ExecuteReturnBigIdentityAsync(cancellationToken);
            }
            else
            {
                pack.PeriodType = periodType;
                pack.TriggerType = triggerType;
                pack.Status = "Pending";
                pack.OverallContentJson = null;
                pack.ErrorSummary = null;
                pack.DurationMs = null;
                pack.UpdatedAt = now;
                await db.Updateable(pack).ExecuteCommandAsync(cancellationToken);
            }

            var existing = await db.Queryable<ReportSection>()
                .Where(item => item.ReportPackId == pack.Id && item.UserId == userId)
                .ToListAsync(cancellationToken);
            foreach (var kind in SectionOrder)
            {
                var section = existing.FirstOrDefault(item => item.Kind == kind);
                if (section is null)
                {
                    await db.Insertable(new ReportSection
                    {
                        UserId = userId,
                        ReportPackId = pack.Id,
                        Kind = kind,
                        Status = "Pending",
                        CreatedAt = now,
                        UpdatedAt = now,
                    }).ExecuteCommandAsync(cancellationToken);
                    continue;
                }
                section.Status = "Pending";
                section.MetricsJson = "{}";
                section.ContentJson = null;
                section.EvidenceJson = "[]";
                section.ErrorSummary = null;
                section.DurationMs = null;
                section.UpdatedAt = now;
                await db.Updateable(section).ExecuteCommandAsync(cancellationToken);
            }
            db.Ado.CommitTran();
            return pack;
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    /// <summary>解析三个手动入口，并统一校验自然日范围。</summary>
    private static ReportRange ResolveRange(GenerateReportRequest request)
    {
        var today = LocalToday();
        DateOnly start;
        DateOnly end;
        string type;
        switch (request.Preset?.Trim())
        {
            case "Today":
                // 日报告覆盖「今天到此刻」；同一天重复生成会复用并覆盖同一个报告包。
                start = today;
                end = today;
                type = "Daily";
                break;
            case "Last7Days":
                start = today.AddDays(-6);
                end = today;
                type = "Weekly";
                break;
            case "Last30Days":
                start = today.AddDays(-29);
                end = today;
                type = "Monthly";
                break;
            case "Custom":
                if (!request.StartDate.HasValue || !request.EndDate.HasValue)
                    throw new ArgumentException("请选择开始和结束日期。");
                start = request.StartDate.Value;
                end = request.EndDate.Value;
                type = "CustomRange";
                break;
            default:
                throw new ArgumentException("报告范围只能是今天、最近 7 天、最近 30 天或自定义。");
        }
        if (start > end) throw new ArgumentException("开始日期不能晚于结束日期。");
        if (end > today) throw new ArgumentException("结束日期不能晚于今天。");
        if (end.DayNumber - start.DayNumber + 1 > 90)
            throw new ArgumentException("一次最多生成 90 天的报告。");
        return new ReportRange(type, start, end);
    }

    private static ReportSectionItem ToSection(ReportSection item)
    {
        var evidence = DeserializeEvidence(item.EvidenceJson).Select(source => new EvidenceItem(
            source.Ref,
            source.OccurredAt,
            source.SourceType,
            source.Text,
            source.AttachmentIds.Select(id => $"/api/assets/{id}/content").ToArray(),
            source.AttachmentDescriptions)).ToArray();
        return new ReportSectionItem(
            item.Kind,
            item.Status,
            JsonSerializer.Deserialize<JsonElement>(item.MetricsJson),
            DeserializeContent(item.ContentJson),
            evidence,
            item.ErrorSummary,
            item.Status == "Failed");
    }

    private static ReportContent? DeserializeContent(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ReportContent>(json, JsonOptions);

    private static ReportEvidence[] DeserializeEvidence(string json) =>
        JsonSerializer.Deserialize<ReportEvidence[]>(json, JsonOptions) ?? [];

    private static WellbeingItem ToWellbeing(WellbeingAssessment item) =>
        new(
            item.Id,
            item.Answers,
            item.RawScore,
            item.PercentageScore,
            item.AssessedAt,
            item.PercentageScore < 50
                ? "当前得分低于 50。WHO-5 官方资料建议考虑进一步专业评估；这不是疾病诊断。"
                : "分数越高，表示过去两周自评的心理幸福感越高。");

    private static DateOnly LocalToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ShanghaiTimeZone).DateTime);

    public sealed record ReportListItem(
        long Id,
        string PeriodType,
        DateOnly StartDate,
        DateOnly EndDate,
        string Status,
        string? Headline,
        int CompletedSectionCount,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record ReportDetail(
        long Id,
        string PeriodType,
        DateOnly StartDate,
        DateOnly EndDate,
        string TriggerType,
        string Status,
        ReportContent? Overall,
        IReadOnlyList<EvidenceItem> OverallEvidence,
        IReadOnlyList<ReportSectionItem> Sections,
        string? Error,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record ReportSectionItem(
        string Kind,
        string Status,
        JsonElement Metrics,
        ReportContent? Content,
        IReadOnlyList<EvidenceItem> Evidence,
        string? Error,
        bool CanRetry);

    public sealed record EvidenceItem(
        string Ref,
        DateTimeOffset OccurredAt,
        string SourceType,
        string Text,
        string[] ImageUrls,
        string[] ImageDescriptions);

    public sealed record WellbeingItem(
        long Id,
        int[] Answers,
        int RawScore,
        int PercentageScore,
        DateTimeOffset AssessedAt,
        string Interpretation);

    private sealed record ReportRange(
        string PeriodType,
        DateOnly StartDate,
        DateOnly EndDate);

    private static readonly string[] SectionOrder =
        ["Life", "Emotion", "Relationship", "Recognition"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeZoneInfo ShanghaiTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
}
