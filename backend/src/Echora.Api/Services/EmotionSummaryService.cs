using System.Text.Json;
using Echora.Api.Entities;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>从情绪原始记录确定性重建日/月投影，不调用模型。</summary>
public sealed class EmotionSummaryService(ISqlSugarClient db)
{
    /// <summary>重建一个自然日和它所属月份的投影。</summary>
    public async Task RebuildAsync(long userId, DateOnly day, CancellationToken cancellationToken)
    {
        var timeZone = GetTimeZone();
        var month = new DateOnly(day.Year, day.Month, 1);
        var start = ToUtcBoundary(month, timeZone);
        var end = ToUtcBoundary(month.AddMonths(1), timeZone);
        var records = await db.Queryable<EmotionRecord>()
            .Where(item => item.UserId == userId && item.OccurredAt >= start && item.OccurredAt < end)
            .ToListAsync(cancellationToken);
        var local = records.Select(item => new LocalEmotion(item, TimeZoneInfo.ConvertTime(item.OccurredAt, timeZone))).ToArray();
        await SaveAsync(userId, "Day", day, BuildDay(local, day), cancellationToken);
        await SaveAsync(userId, "Month", month, BuildMonth(local, month), cancellationToken);
    }

    /// <summary>返回一个会话当前影响到的用户本地日期。</summary>
    public async Task<IReadOnlyList<DateOnly>> GetConversationDaysAsync(
        long userId,
        long conversationId,
        CancellationToken cancellationToken)
    {
        var timeZone = GetTimeZone();
        return (await db.Queryable<EmotionRecord>()
                .Where(item => item.UserId == userId && item.ConversationId == conversationId)
                .ToListAsync(cancellationToken))
            .Select(item => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.OccurredAt, timeZone).DateTime))
            .Distinct()
            .ToArray();
    }

    /// <summary>补算没有投影或投影早于原始记录的日期。</summary>
    public async Task RepairMissingAsync(CancellationToken cancellationToken)
    {
        var timeZone = GetTimeZone();
        var records = await db.Queryable<EmotionRecord>().ToListAsync(cancellationToken);
        foreach (var userRecords in records.GroupBy(item => item.UserId))
        {
            var summaries = await db.Queryable<EmotionSummary>()
                .Where(item => item.UserId == userRecords.Key)
                .ToListAsync(cancellationToken);
            var days = userRecords
                .GroupBy(item => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.OccurredAt, timeZone).DateTime))
                .Where(group => summaries.All(summary => summary.PeriodType != "Day" || summary.PeriodStart != group.Key)
                    || summaries.First(summary => summary.PeriodType == "Day" && summary.PeriodStart == group.Key).UpdatedAt < group.Max(item => item.UpdatedAt))
                .Select(group => group.Key)
                .ToArray();
            foreach (var day in days)
                await RebuildAsync(userRecords.Key, day, cancellationToken);
        }
    }

    /// <summary>保存或覆盖一个确定性投影。</summary>
    private async Task SaveAsync(
        long userId,
        string periodType,
        DateOnly periodStart,
        Projection projection,
        CancellationToken cancellationToken)
    {
        var existing = await db.Queryable<EmotionSummary>()
            .Where(item => item.UserId == userId
                && item.PeriodType == periodType
                && item.PeriodStart == periodStart)
            .FirstAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            await db.Insertable(new EmotionSummary
            {
                UserId = userId,
                PeriodType = periodType,
                PeriodStart = periodStart,
                Summary = projection.Summary,
                MetricsJson = JsonSerializer.Serialize(projection.Metrics),
                UpdatedAt = now,
            }).ExecuteCommandAsync(cancellationToken);
            return;
        }
        existing.Summary = projection.Summary;
        existing.MetricsJson = JsonSerializer.Serialize(projection.Metrics);
        existing.UpdatedAt = now;
        await db.Updateable(existing).ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>生成一天的家族次数、峰值和子类。</summary>
    private static Projection BuildDay(IReadOnlyList<LocalEmotion> records, DateOnly day)
    {
        var current = records.Where(item => DateOnly.FromDateTime(item.Local.DateTime) == day).ToArray();
        var metrics = current.GroupBy(item => item.Record.Family).Select(group => new
        {
            family = group.Key,
            momentCount = group.Count(),
            peakIntensity = group.Max(item => item.Record.Intensity),
            subtypes = group.Select(item => item.Record.Subtype).Distinct().ToArray(),
        }).ToArray();
        var summary = current.Length == 0 ? null : $"这一天记录到 {metrics.Length} 个情绪家族、{current.Length} 个情绪时刻。";
        return new Projection(summary, metrics);
    }

    /// <summary>生成一个月的出现天数和平均日峰值。</summary>
    private static Projection BuildMonth(IReadOnlyList<LocalEmotion> records, DateOnly month)
    {
        var current = records.Where(item => item.Local.Year == month.Year && item.Local.Month == month.Month).ToArray();
        var metrics = current.GroupBy(item => item.Record.Family).Select(group =>
        {
            var days = group.GroupBy(item => DateOnly.FromDateTime(item.Local.DateTime)).ToArray();
            return new
            {
                family = group.Key,
                activeDays = days.Length,
                averageDailyPeak = days.Length == 0 ? 0 : Math.Round(days.Average(day => day.Max(item => item.Record.Intensity)), 1),
            };
        }).ToArray();
        var activeDays = current.Select(item => DateOnly.FromDateTime(item.Local.DateTime)).Distinct().Count();
        var summary = current.Length == 0 ? null : $"本月有 {activeDays} 天留下明确情绪记录。";
        return new Projection(summary, metrics);
    }

    /// <summary>当前产品统一使用上海时区生成日/月投影。</summary>
    private static TimeZoneInfo GetTimeZone() =>
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    /// <summary>把上海本地日期边界转换成可使用时间索引过滤的 UTC 时间。</summary>
    private static DateTimeOffset ToUtcBoundary(DateOnly date, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>一个可直接写入数据库的投影结果。</summary>
    private sealed record Projection(string? Summary, object Metrics);
    /// <summary>附带用户本地时间的情绪记录。</summary>
    private sealed record LocalEmotion(EmotionRecord Record, DateTimeOffset Local);
}
