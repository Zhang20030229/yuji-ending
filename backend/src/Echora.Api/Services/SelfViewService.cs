using System.Text.Json;
using Echora.Api.Authentication;
using Echora.Api.Contracts;
using Echora.Api.Entities;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>为遇己页面组合认识和情绪日/月数据。</summary>
public sealed class SelfViewService(ISqlSugarClient db, IHttpContextAccessor accessor, MemoryEmbeddingService embeddings)
{
    private long UserId => accessor.HttpContext?.User.GetRequiredId()
        ?? throw new UnauthorizedAccessException("当前请求没有用户身份。");

    /// <summary>驳回一条认识；不改动原话，只让它退出所有引用通道。</summary>
    public async Task<bool> RejectRecognitionAsync(long id, string? note, CancellationToken cancellationToken)
    {
        var trimmed = Clean(note);
        if (trimmed is { Length: > 500 })
            throw new ArgumentException("驳回说明不能超过 500 字。");
        var now = DateTimeOffset.UtcNow;
        var affected = await db.Updateable<Recognition>()
            .SetColumns(item => new Recognition { RejectedAt = now, RejectionNote = trimmed, UpdatedAt = now })
            .Where(item => item.Id == id && item.UserId == UserId)
            .ExecuteCommandAsync(cancellationToken);
        if (affected == 0) return false;
        // 立即删除向量，否则回填任务会在下一轮把它当作缺失向量写回语义召回池。
        await embeddings.RemoveAsync("recognition", id, UserId, cancellationToken);
        return true;
    }

    /// <summary>撤销驳回；向量由回填任务自动补回，不在此处重算。</summary>
    public async Task<bool> RestoreRecognitionAsync(long id, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.Updateable<Recognition>()
            .SetColumns(item => new Recognition { RejectedAt = null, RejectionNote = null, UpdatedAt = now })
            .Where(item => item.Id == id && item.UserId == UserId)
            .ExecuteCommandAsync(cancellationToken) > 0;
    }

    /// <summary>按分类和关键词读取认识及其原话；默认只返回未被驳回的认识。</summary>
    public async Task<IReadOnlyList<RecognitionItem>> GetRecognitionsAsync(
        string? category,
        string? query,
        CancellationToken cancellationToken,
        string? rejected = null)
    {
        var recognitionQuery = db.Queryable<Recognition>()
            .Where(item => item.UserId == UserId);
        recognitionQuery = rejected switch
        {
            "true" => recognitionQuery.Where(item => item.RejectedAt != null),
            "all" => recognitionQuery,
            _ => recognitionQuery.Where(item => item.RejectedAt == null),
        };
        if (!string.IsNullOrWhiteSpace(category))
            recognitionQuery = recognitionQuery.Where(item => item.Category == category);
        var rows = await recognitionQuery
            .OrderBy(item => item.UpdatedAt, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(query))
            rows = rows.Where(item => Contains(item.Content, query) || item.Keywords.Any(value => Contains(value, query))).ToList();
        var conversationIds = rows.Where(item => item.ConversationId.HasValue).Select(item => item.ConversationId!.Value).Distinct().ToArray();
        var conversations = conversationIds.Length == 0
            ? []
            : await db.Queryable<Conversation>()
                .Where(item => item.UserId == UserId && conversationIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
        var conversationTitles = conversations.ToDictionary(item => item.Id, item => item.Title);
        var quotes = await LoadQuotesAsync(
            rows.Select(item => (item.SourceMessageId, item.SourceMomentId)),
            cancellationToken);
        return rows.Select(item => new RecognitionItem(
                item.Id,
                item.Category,
                item.Content,
                item.Keywords,
                item.ConversationId ?? 0,
                item.ConversationId.HasValue
                    && conversationTitles.TryGetValue(item.ConversationId.Value, out var title)
                    ? title
                    : "一刻",
                item.UpdatedAt,
                GetQuotes(quotes, item.SourceMessageId, item.SourceMomentId),
                item.RejectedAt,
                item.RejectionNote))
            .ToArray();
    }

    /// <summary>读取一个本地自然日的情绪时刻和家族统计。</summary>
    public async Task<EmotionDay> GetEmotionDayAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var zone = GetTimeZone();
        var start = ToUtcBoundary(date, zone);
        var end = ToUtcBoundary(date.AddDays(1), zone);
        var records = (await db.Queryable<EmotionRecord>()
                .Where(item => item.UserId == UserId && item.OccurredAt >= start && item.OccurredAt < end)
                .ToListAsync(cancellationToken))
            .Select(item => new LocalEmotion(item, TimeZoneInfo.ConvertTime(item.OccurredAt, zone)))
            .OrderBy(item => item.Local)
            .ToArray();
        var observations = (await db.Queryable<CbtObservation>()
                .Where(item => item.UserId == UserId && item.OccurredAt >= start && item.OccurredAt < end)
                .OrderBy(item => item.OccurredAt)
                .ToListAsync(cancellationToken))
            .ToArray();
        var conversationIds = records.Where(item => item.Record.ConversationId.HasValue).Select(item => item.Record.ConversationId!.Value).Distinct().ToArray();
        var conversations = conversationIds.Length == 0
            ? []
            : await db.Queryable<Conversation>()
                .Where(item => item.UserId == UserId && conversationIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
        var conversationTitles = conversations.ToDictionary(item => item.Id, item => item.Title);
        var quotes = await LoadQuotesAsync(
            records.Select(item => (item.Record.SourceMessageId, item.Record.SourceMomentId))
                .Concat(observations.Select(item => (item.SourceMessageId, item.SourceMomentId))),
            cancellationToken);
        var items = records.Select(item => new EmotionItem(
                item.Record.Id,
                item.Record.Family,
                item.Record.Subtype,
                item.Record.Intensity,
                item.Record.Summary,
                item.Local,
                item.Record.ConversationId ?? 0,
                item.Record.ConversationId.HasValue
                    && conversationTitles.TryGetValue(item.Record.ConversationId.Value, out var title)
                    ? title
                    : "一刻",
                GetQuotes(quotes, item.Record.SourceMessageId, item.Record.SourceMomentId)))
            .ToArray();
        var families = records.GroupBy(item => item.Record.Family).Select(group => new FamilyDayStat(
            group.Key,
            group.Count(),
            group.Max(item => item.Record.Intensity),
            group.Select(item => item.Record.Subtype).Distinct().ToArray())).ToArray();
        var summary = await db.Queryable<EmotionSummary>()
            .Where(item => item.UserId == UserId && item.PeriodType == "Day" && item.PeriodStart == date)
            .FirstAsync(cancellationToken);
        var digest = await db.Queryable<DayDigest>()
            .Where(item => item.UserId == UserId && item.Date == date)
            .FirstAsync(cancellationToken);
        return new EmotionDay(
            date,
            summary?.Summary,
            string.IsNullOrWhiteSpace(digest?.Narrative) ? null : digest.Narrative,
            records.Select(item => item.Record.ConversationId).Where(id => id.HasValue).Distinct().Count(),
            records.Select(item => (item.Record.SourceMessageId, item.Record.SourceMomentId))
                .Concat(observations.Select(item => (item.SourceMessageId, item.SourceMomentId)))
                .Distinct()
                .Count(),
            items,
            families,
            ParseInsights(digest),
            observations.Select(item => ToCbtObservation(item, quotes)).ToArray());
    }

    /// <summary>读取已生成的关键洞察；缺失或解析失败时返回空列表，由页面回退到原始观察。</summary>
    private static IReadOnlyList<DayInsightItem> ParseInsights(DayDigest? digest)
    {
        if (string.IsNullOrWhiteSpace(digest?.InsightsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<DayInsightItem[]>(digest.InsightsJson, InsightJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static readonly JsonSerializerOptions InsightJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>读取一个月的情绪日历、趋势和代表原话。</summary>
    public async Task<EmotionMonth> GetEmotionMonthAsync(DateOnly month, CancellationToken cancellationToken)
    {
        month = new DateOnly(month.Year, month.Month, 1);
        var zone = GetTimeZone();
        var start = ToUtcBoundary(month, zone);
        var end = ToUtcBoundary(month.AddMonths(1), zone);
        var records = (await db.Queryable<EmotionRecord>()
                .Where(item => item.UserId == UserId && item.OccurredAt >= start && item.OccurredAt < end)
                .ToListAsync(cancellationToken))
            .Select(item => new LocalEmotion(item, TimeZoneInfo.ConvertTime(item.OccurredAt, zone)))
            .ToArray();
        var days = records.GroupBy(item => DateOnly.FromDateTime(item.Local.DateTime)).OrderBy(group => group.Key).Select(group => new EmotionCalendarDay(
            group.Key,
            group.GroupBy(item => item.Record.Family).Select(family => new CalendarDot(
                family.Key,
                family.Max(item => item.Record.Intensity),
                family.Count(),
                family.Max(item => item.Local))).ToArray())).ToArray();
        var families = records.GroupBy(item => item.Record.Family).Select(group =>
        {
            var byDay = group.GroupBy(item => DateOnly.FromDateTime(item.Local.DateTime)).OrderBy(day => day.Key).ToArray();
            return new FamilyMonthStat(
                group.Key,
                byDay.Length,
                byDay.Length == 0 ? 0 : Math.Round(byDay.Average(day => day.Max(item => item.Record.Intensity)), 1),
                group.Select(item => item.Record.Subtype).Distinct().ToArray(),
                byDay.Select(day => new TrendPoint(day.Key, day.Max(item => item.Record.Intensity))).ToArray());
        }).ToArray();
        var peaks = records
            .GroupBy(item => DateOnly.FromDateTime(item.Local.DateTime))
            .Select(day => (Date: day.Key, Peak: day.OrderByDescending(item => item.Record.Intensity).First()))
            .ToArray();
        var quotes = await LoadQuotesAsync(
            peaks.Select(item => (item.Peak.Record.SourceMessageId, item.Peak.Record.SourceMomentId)),
            cancellationToken);
        var representative = new List<RepresentativeQuote>(peaks.Length);
        foreach (var item in peaks)
        {
            var peakQuotes = GetQuotes(
                quotes,
                item.Peak.Record.SourceMessageId,
                item.Peak.Record.SourceMomentId);
            representative.Add(new RepresentativeQuote(
                item.Date,
                item.Peak.Record.Family,
                item.Peak.Record.Subtype,
                peakQuotes.FirstOrDefault()?.Text ?? item.Peak.Record.Summary));
        }
        var summary = await db.Queryable<EmotionSummary>()
            .Where(item => item.UserId == UserId && item.PeriodType == "Month" && item.PeriodStart == month)
            .FirstAsync(cancellationToken);
        var cbtObservations = await db.Queryable<CbtObservation>()
            .Where(item => item.UserId == UserId && item.OccurredAt >= start && item.OccurredAt < end)
            .ToListAsync(cancellationToken);
        return new EmotionMonth(
            month,
            summary?.Summary,
            days,
            families,
            representative,
            cbtObservations.Count,
            cbtObservations
                .Select(item => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.OccurredAt, zone).DateTime))
                .Distinct()
                .Count());
    }

    /// <summary>修订一条 CBT 自我观察，不修改原消息或一刻。</summary>
    public async Task<bool> UpdateCbtObservationAsync(
        long id,
        UpdateCbtObservationRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.Queryable<CbtObservation>()
            .Where(row => row.Id == id && row.UserId == UserId)
            .FirstAsync(cancellationToken);
        if (item is null) return false;
        item.Situation = request.Situation?.Trim() ?? "";
        item.AutomaticThought = Clean(request.AutomaticThought);
        item.BodySensation = Clean(request.BodySensation);
        item.Behavior = Clean(request.Behavior);
        item.ImmediateOutcome = Clean(request.ImmediateOutcome);
        if (item.Situation.Length == 0)
            throw new ArgumentException("发生了什么不能为空。");
        if (item.AutomaticThought is null
            && item.BodySensation is null
            && item.Behavior is null
            && item.ImmediateOutcome is null)
            throw new ArgumentException("请至少保留一项想法、身体感受、行为或直接结果。");
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(item).ExecuteCommandAsync(cancellationToken);
        return true;
    }

    /// <summary>删除一条 CBT 自我观察，不修改原消息、情绪或一刻。</summary>
    public async Task<bool> DeleteCbtObservationAsync(long id, CancellationToken cancellationToken) =>
        await db.Deleteable<CbtObservation>()
            .Where(item => item.Id == id && item.UserId == UserId)
            .ExecuteCommandAsync(cancellationToken) > 0;

    /// <summary>读取指定年份的情绪活跃日、月份和家族统计。</summary>
    public async Task<EmotionYear> GetEmotionYearAsync(int year, CancellationToken cancellationToken)
    {
        if (year is < 1900 or > 9999) throw new ArgumentOutOfRangeException(nameof(year));

        var zone = GetTimeZone();
        var startDate = new DateOnly(year, 1, 1);
        var start = ToUtcBoundary(startDate, zone);
        var end = ToUtcBoundary(startDate.AddYears(1), zone);
        var records = (await db.Queryable<EmotionRecord>()
                .Where(item => item.UserId == UserId && item.OccurredAt >= start && item.OccurredAt < end)
                .ToListAsync(cancellationToken))
            .Select(item => new LocalEmotion(item, TimeZoneInfo.ConvertTime(item.OccurredAt, zone)))
            .ToArray();

        var days = records
            .GroupBy(item => DateOnly.FromDateTime(item.Local.DateTime))
            .OrderBy(group => group.Key)
            .Select(group => new EmotionYearDay(
                group.Key,
                group.GroupBy(item => item.Record.Family)
                    .Select(family => new CalendarDot(
                        family.Key,
                        family.Max(item => item.Record.Intensity),
                        family.Count(),
                        family.Max(item => item.Local)))
                    .ToArray()))
            .ToArray();
        var months = records
            .GroupBy(item => item.Local.Month)
            .OrderBy(group => group.Key)
            .Select(group => new EmotionYearMonth(
                group.Key,
                group.Select(item => item.Local.Date).Distinct().Count(),
                group.Count()))
            .ToArray();
        var families = records
            .GroupBy(item => item.Record.Family)
            .Select(group => new EmotionYearFamily(
                group.Key,
                group.Select(item => item.Local.Date).Distinct().Count(),
                group.Max(item => item.Record.Intensity),
                group.Count()))
            .ToArray();

        return new EmotionYear(year, days, months, families);
    }

    /// <summary>一次性读取一批聊天消息和一刻原话，避免列表按条访问数据库。</summary>
    private async Task<Dictionary<(long? MessageId, long? MomentId), IReadOnlyList<Quote>>> LoadQuotesAsync(
        IEnumerable<(long? MessageId, long? MomentId)> sources,
        CancellationToken cancellationToken)
    {
        var keys = sources.Distinct().ToArray();
        var messageIds = keys.Where(item => item.MessageId.HasValue).Select(item => item.MessageId!.Value).Distinct().ToArray();
        var momentIds = keys.Where(item => item.MomentId.HasValue).Select(item => item.MomentId!.Value).Distinct().ToArray();
        var messages = messageIds.Length == 0
            ? []
            : await db.Queryable<ConversationMessage>()
                .Where(item => item.UserId == UserId && messageIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
        var moments = momentIds.Length == 0
            ? []
            : await db.Queryable<Moment>()
                .Where(item => item.UserId == UserId && momentIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
        var messageById = messages.ToDictionary(item => item.Id);
        var momentById = moments.ToDictionary(item => item.Id);
        var result = new Dictionary<(long?, long?), IReadOnlyList<Quote>>();
        foreach (var key in keys)
        {
            if (key.MessageId is long messageId && messageById.TryGetValue(messageId, out var message))
                result[key] = [new Quote(message.Id, "Message", message.Text ?? string.Empty, message.CreatedAt, message.LocationName, message.LocationAddress)];
            else if (key.MomentId is long momentId && momentById.TryGetValue(momentId, out var moment))
                result[key] = [new Quote(moment.Id, "Moment", moment.Text ?? string.Empty, moment.PublishedAt, moment.LocationName, moment.LocationAddress)];
            else
                result[key] = [];
        }
        return result;
    }

    /// <summary>读取批量来源字典中的一条记录。</summary>
    private static IReadOnlyList<Quote> GetQuotes(
        IReadOnlyDictionary<(long? MessageId, long? MomentId), IReadOnlyList<Quote>> quotes,
        long? messageId,
        long? momentId) =>
        quotes.TryGetValue((messageId, momentId), out var result) ? result : [];

    /// <summary>把上海本地日期边界转换成可直接用于 PostgreSQL 索引查询的 UTC 时间。</summary>
    private static DateTimeOffset ToUtcBoundary(DateOnly date, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>当前产品统一使用上海时区展示情绪。</summary>
    private static TimeZoneInfo GetTimeZone() =>
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    private static bool Contains(string? value, string query) => value?.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>把数据库观察和批量读取的原话组合成页面对象。</summary>
    private static CbtObservationItem ToCbtObservation(
        CbtObservation item,
        IReadOnlyDictionary<(long? MessageId, long? MomentId), IReadOnlyList<Quote>> quotes) =>
        new(
            item.Id,
            item.Situation,
            item.AutomaticThought,
            item.BodySensation,
            item.Behavior,
            item.ImmediateOutcome,
            item.OccurredAt,
            GetQuotes(quotes, item.SourceMessageId, item.SourceMomentId));

    /// <summary>把空白可选字段统一转换为空值。</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed record RecognitionItem(long Id, string Category, string Content, IReadOnlyList<string> Keywords, long ConversationId, string ConversationTitle, DateTimeOffset UpdatedAt, IReadOnlyList<Quote> Sources, DateTimeOffset? RejectedAt, string? RejectionNote);
    public sealed record Quote(long MessageId, string SourceType, string Text, DateTimeOffset CreatedAt, string? LocationName, string? LocationAddress);
    public sealed record EmotionDay(DateOnly Date, string? Summary, string? Narrative, int ConversationCount, int SourceMessageCount, IReadOnlyList<EmotionItem> Items, IReadOnlyList<FamilyDayStat> Families, IReadOnlyList<DayInsightItem> Insights, IReadOnlyList<CbtObservationItem> CbtObservations);
    public sealed record DayInsightItem(string Situation, string? Appraisal, IReadOnlyList<string> Emotions, string? FollowUp, IReadOnlyList<string> Quotes, IReadOnlyList<long> ObservationIds);
    public sealed record EmotionItem(long Id, string Family, string Subtype, short Intensity, string Summary, DateTimeOffset OccurredAt, long ConversationId, string ConversationTitle, IReadOnlyList<Quote> Sources);
    public sealed record FamilyDayStat(string Family, int MomentCount, short PeakIntensity, IReadOnlyList<string> Subtypes);
    public sealed record EmotionMonth(DateOnly Month, string? Summary, IReadOnlyList<EmotionCalendarDay> Days, IReadOnlyList<FamilyMonthStat> Families, IReadOnlyList<RepresentativeQuote> RepresentativeQuotes, int CbtObservationCount, int CbtCoveredDays);
    public sealed record CbtObservationItem(long Id, string Situation, string? AutomaticThought, string? BodySensation, string? Behavior, string? ImmediateOutcome, DateTimeOffset OccurredAt, IReadOnlyList<Quote> Sources);
    public sealed record EmotionCalendarDay(DateOnly Date, IReadOnlyList<CalendarDot> Families);
    public sealed record CalendarDot(string Family, short PeakIntensity, int Count, DateTimeOffset LastOccurredAt);
    public sealed record FamilyMonthStat(string Family, int ActiveDays, double AverageDailyPeak, IReadOnlyList<string> Subtypes, IReadOnlyList<TrendPoint> Trend);
    public sealed record TrendPoint(DateOnly Date, short PeakIntensity);
    public sealed record RepresentativeQuote(DateOnly Date, string Family, string Subtype, string Text);
    public sealed record EmotionYear(int Year, IReadOnlyList<EmotionYearDay> Days, IReadOnlyList<EmotionYearMonth> Months, IReadOnlyList<EmotionYearFamily> Families);
    public sealed record EmotionYearDay(DateOnly Date, IReadOnlyList<CalendarDot> Families);
    public sealed record EmotionYearMonth(int Month, int ActiveDays, int RecordCount);
    public sealed record EmotionYearFamily(string Family, int ActiveDays, short PeakIntensity, int RecordCount);
    private sealed record LocalEmotion(EmotionRecord Record, DateTimeOffset Local);
}
