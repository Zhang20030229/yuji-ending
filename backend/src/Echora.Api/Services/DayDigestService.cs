using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echora.Api.Agents;
using Echora.Api.Entities;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>按输入指纹增量重建洞察日视图使用的当日 AI 摘要。</summary>
public sealed class DayDigestService(
    ISqlSugarClient db,
    DayDigestAgent agent,
    ILogger<DayDigestService> logger)
{
    /// <summary>重建一天的摘要；输入未变化时直接跳过，不调用模型。</summary>
    public async Task RebuildAsync(long userId, DateOnly day, CancellationToken cancellationToken)
    {
        var zone = GetTimeZone();
        var start = ToUtcBoundary(day, zone);
        var end = ToUtcBoundary(day.AddDays(1), zone);
        var records = (await db.Queryable<EmotionRecord>()
                .Where(item => item.UserId == userId && item.OccurredAt >= start && item.OccurredAt < end)
                .ToListAsync(cancellationToken))
            .OrderBy(item => item.OccurredAt)
            .ToArray();
        var observations = (await db.Queryable<CbtObservation>()
                .Where(item => item.UserId == userId && item.OccurredAt >= start && item.OccurredAt < end)
                .ToListAsync(cancellationToken))
            .OrderBy(item => item.OccurredAt)
            .ToArray();
        var existing = await db.Queryable<DayDigest>()
            .Where(item => item.UserId == userId && item.Date == day)
            .FirstAsync(cancellationToken);

        if (records.Length == 0 && observations.Length == 0)
        {
            if (existing is not null)
                await db.Deleteable(existing).ExecuteCommandAsync(cancellationToken);
            return;
        }

        var fingerprint = Fingerprint(records, observations);
        if (existing?.SourceFingerprint == fingerprint)
        {
            logger.LogInformation(
                "Day digest skipped, fingerprint unchanged: UserId {UserId}, Day {Day}",
                userId,
                day);
            return;
        }

        var texts = await LoadTextsAsync(userId, records, observations, cancellationToken);
        var context = BuildContext(day, records, observations, texts, zone);
        var result = await agent.RunAsync(context, cancellationToken);
        // 模型失败时保留上一次摘要，页面继续显示旧内容而不是退化成空。
        if (result is null) return;

        var insights = Sanitize(result, records, observations, texts, zone);
        var narrative = (result.Narrative ?? string.Empty).Trim();
        if (narrative.Length > NarrativeLimit) narrative = string.Empty;
        var payload = JsonSerializer.Serialize(insights, JsonOptions);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            await db.Insertable(new DayDigest
            {
                UserId = userId,
                Date = day,
                Narrative = narrative,
                InsightsJson = payload,
                SourceFingerprint = fingerprint,
                UpdatedAt = now,
            }).ExecuteCommandAsync(cancellationToken);
        }
        else
        {
            existing.Narrative = narrative;
            existing.InsightsJson = payload;
            existing.SourceFingerprint = fingerprint;
            existing.UpdatedAt = now;
            await db.Updateable(existing).ExecuteCommandAsync(cancellationToken);
        }
        logger.LogInformation(
            "Day digest rebuilt: UserId {UserId}, Day {Day}, InsightCount {InsightCount}, NarrativeLength {NarrativeLength}",
            userId,
            day,
            insights.Length,
            narrative.Length);
    }

    /// <summary>补算最近若干天缺失或过期的摘要；每个用户的天数有上限，避免一次巡检产生大量模型调用。</summary>
    public async Task RepairRecentAsync(int maxDaysPerUser, CancellationToken cancellationToken)
    {
        var zone = GetTimeZone();
        var recordDays = (await db.Queryable<EmotionRecord>().ToListAsync(cancellationToken))
            .Select(item => (item.UserId, Day: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.OccurredAt, zone).DateTime)));
        var observationDays = (await db.Queryable<CbtObservation>().ToListAsync(cancellationToken))
            .Select(item => (item.UserId, Day: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.OccurredAt, zone).DateTime)));
        var candidates = recordDays.Concat(observationDays)
            .Distinct()
            .GroupBy(item => item.UserId)
            .SelectMany(group => group.Select(item => item.Day).OrderDescending().Take(maxDaysPerUser).Select(day => (group.Key, day)));
        foreach (var (userId, day) in candidates)
        {
            try
            {
                // RebuildAsync 内部按指纹跳过，已是最新的日期不会再调用模型。
                await RebuildAsync(userId, day, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Day digest repair failed: UserId {UserId}, Day {Day}", userId, day);
            }
        }
    }

    /// <summary>删除指定日期的摘要；下一次分析会按需重建。</summary>
    public async Task RemoveAsync(long userId, IReadOnlyCollection<DateOnly> days, CancellationToken cancellationToken)
    {
        if (days.Count == 0) return;
        await db.Deleteable<DayDigest>()
            .Where(item => item.UserId == userId && days.Contains(item.Date))
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>删除一个用户的全部摘要。</summary>
    public Task RemoveAllAsync(long userId, CancellationToken cancellationToken) =>
        db.Deleteable<DayDigest>().Where(item => item.UserId == userId).ExecuteCommandAsync(cancellationToken);

    /// <summary>输入只由情绪记录和 CBT 观察决定；任一条新增、修改或删除都会改变指纹。</summary>
    private static string Fingerprint(
        IReadOnlyList<EmotionRecord> records,
        IReadOnlyList<CbtObservation> observations)
    {
        var payload = string.Join('|', records
            .OrderBy(item => item.Id)
            .Select(item => $"e{item.Id}:{item.UpdatedAt.UtcTicks}")
            .Concat(observations
                .OrderBy(item => item.Id)
                .Select(item => $"c{item.Id}:{item.UpdatedAt.UtcTicks}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>一次性读取当天用到的聊天原话与一刻原文。</summary>
    private async Task<Dictionary<(long? MessageId, long? MomentId), string>> LoadTextsAsync(
        long userId,
        IReadOnlyList<EmotionRecord> records,
        IReadOnlyList<CbtObservation> observations,
        CancellationToken cancellationToken)
    {
        var keys = records.Select(item => (item.SourceMessageId, item.SourceMomentId))
            .Concat(observations.Select(item => (item.SourceMessageId, item.SourceMomentId)))
            .Distinct()
            .ToArray();
        var messageIds = keys.Where(item => item.Item1.HasValue).Select(item => item.Item1!.Value).Distinct().ToArray();
        var momentIds = keys.Where(item => item.Item2.HasValue).Select(item => item.Item2!.Value).Distinct().ToArray();
        var messages = messageIds.Length == 0
            ? []
            : await db.Queryable<ConversationMessage>()
                .Where(item => item.UserId == userId && messageIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
        var moments = momentIds.Length == 0
            ? []
            : await db.Queryable<Moment>()
                .Where(item => item.UserId == userId && momentIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
        var messageById = messages.ToDictionary(item => item.Id, item => item.Text ?? string.Empty);
        var momentById = moments.ToDictionary(item => item.Id, item => item.Text ?? string.Empty);
        var result = new Dictionary<(long?, long?), string>();
        foreach (var key in keys)
        {
            if (key.Item1 is long messageId && messageById.TryGetValue(messageId, out var messageText))
                result[key] = messageText;
            else if (key.Item2 is long momentId && momentById.TryGetValue(momentId, out var momentText))
                result[key] = momentText;
            else
                result[key] = string.Empty;
        }
        return result;
    }

    /// <summary>按时间顺序组装模型输入；只放模型需要的字段，不放会话标题等无关信息。</summary>
    private static string BuildContext(
        DateOnly day,
        IReadOnlyList<EmotionRecord> records,
        IReadOnlyList<CbtObservation> observations,
        IReadOnlyDictionary<(long? MessageId, long? MomentId), string> texts,
        TimeZoneInfo zone)
    {
        var context = new
        {
            date = day.ToString("yyyy-MM-dd"),
            emotionRecords = records.Select(item => new
            {
                time = LocalTime(item.OccurredAt, zone),
                family = item.Family,
                subtype = item.Subtype,
                intensity = item.Intensity,
                summary = item.Summary,
                quote = Text(texts, item.SourceMessageId, item.SourceMomentId),
            }).ToArray(),
            cbtObservations = observations.Select(item => new
            {
                observationId = item.Id,
                time = LocalTime(item.OccurredAt, zone),
                situation = item.Situation,
                automaticThought = item.AutomaticThought,
                bodySensation = item.BodySensation,
                behavior = item.Behavior,
                immediateOutcome = item.ImmediateOutcome,
                quote = Text(texts, item.SourceMessageId, item.SourceMomentId),
            }).ToArray(),
        };
        return JsonSerializer.Serialize(context, JsonOptions);
    }

    /// <summary>模型输出只保留有当天依据的部分；无法验证的字段一律丢弃而不是保留。</summary>
    private static DayInsight[] Sanitize(
        DayDigestAgent.Result result,
        IReadOnlyList<EmotionRecord> records,
        IReadOnlyList<CbtObservation> observations,
        IReadOnlyDictionary<(long? MessageId, long? MomentId), string> texts,
        TimeZoneInfo zone)
    {
        var dayText = string.Join('\n', texts.Values);
        var allowedEmotions = records
            .Select(item => $"{item.Family}·{item.Subtype}")
            .ToHashSet(StringComparer.Ordinal);
        var observationById = observations.ToDictionary(item => item.Id);
        var latest = records.Select(item => item.OccurredAt)
            .Concat(observations.Select(item => item.OccurredAt))
            .DefaultIfEmpty()
            .Max();
        var insights = new List<DayInsight>(3);
        foreach (var item in result.Insights ?? [])
        {
            var situation = Clean(item.Situation);
            if (situation is null) continue;
            var quotes = (item.Quotes ?? [])
                .Select(quote => quote?.Trim() ?? string.Empty)
                .Where(quote => quote.Length > 0 && dayText.Contains(quote, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            var observationIds = (item.ObservationIds ?? [])
                .Where(observationById.ContainsKey)
                .Distinct()
                .ToArray();
            // 一条洞察至少要有一句当天原话或一条真实观察兜底，否则无法证明它来自今天。
            if (quotes.Length == 0 && observationIds.Length == 0) continue;
            var anchor = observationIds.Length == 0
                ? records.FirstOrDefault(record => quotes.Any(quote =>
                    Text(texts, record.SourceMessageId, record.SourceMomentId).Contains(quote, StringComparison.Ordinal)))?.OccurredAt
                : observationIds.Min(id => observationById[id].OccurredAt);
            // 没有更晚的记录时「后续进展」不可能成立，直接丢弃模型的说法。
            var followUp = anchor is DateTimeOffset time && latest > time ? Clean(item.FollowUp) : null;
            insights.Add(new DayInsight(
                situation,
                Clean(item.Appraisal),
                (item.Emotions ?? [])
                    .Select(emotion => emotion?.Trim() ?? string.Empty)
                    .Where(allowedEmotions.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .Take(3)
                    .ToArray(),
                followUp,
                quotes,
                observationIds));
            if (insights.Count == 3) break;
        }
        return [.. insights];
    }

    /// <summary>读取一条记录对应的用户原话。</summary>
    private static string Text(
        IReadOnlyDictionary<(long? MessageId, long? MomentId), string> texts,
        long? messageId,
        long? momentId) =>
        texts.TryGetValue((messageId, momentId), out var text) ? text : string.Empty;

    /// <summary>把 UTC 时间转换成模型可读的本地时刻。</summary>
    private static string LocalTime(DateTimeOffset value, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(value, zone).ToString("HH:mm");

    /// <summary>把空白模型字段统一转换成空值。</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int NarrativeLimit = 120;

    /// <summary>当前产品统一使用上海时区划分自然日。</summary>
    private static TimeZoneInfo GetTimeZone() =>
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    /// <summary>把上海本地日期边界转换成可直接用于时间索引的 UTC 时间。</summary>
    private static DateTimeOffset ToUtcBoundary(DateOnly date, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>一条通过依据校验、可直接返回给页面的关键洞察。</summary>
    public sealed record DayInsight(
        string Situation,
        string? Appraisal,
        IReadOnlyList<string> Emotions,
        string? FollowUp,
        IReadOnlyList<string> Quotes,
        IReadOnlyList<long> ObservationIds);
}
