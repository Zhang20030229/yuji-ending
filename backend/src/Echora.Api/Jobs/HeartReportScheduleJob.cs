using Echora.Api.Entities;
using Echora.Api.Services;
using Hangfire;
using SqlSugar;

namespace Echora.Api.Jobs;

/// <summary>每天幂等检查上一个完整自然周和自然月。</summary>
public sealed class HeartReportScheduleJob(
    ISqlSugarClient db,
    HeartReportService reports,
    ILogger<HeartReportScheduleJob> logger)
{
    /// <summary>周期检查失败由下一天自然补偿，不叠加任务级重试。</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ShanghaiTimeZone).DateTime);
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var currentMonday = today.AddDays(-daysSinceMonday);
        var weekStart = currentMonday.AddDays(-7);
        var weekEnd = currentMonday.AddDays(-1);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var monthStart = currentMonth.AddMonths(-1);
        var monthEnd = currentMonth.AddDays(-1);
        var users = await db.Queryable<UserAccount>().Select(item => item.Id).ToListAsync(cancellationToken);
        var scheduled = 0;
        foreach (var userId in users)
        {
            if (await reports.EnsureAutomaticAsync(
                    userId, "Weekly", weekStart, weekEnd, cancellationToken))
                scheduled++;
            if (await reports.EnsureAutomaticAsync(
                    userId, "Monthly", monthStart, monthEnd, cancellationToken))
                scheduled++;
        }
        logger.LogInformation(
            "Heart report schedule completed: UserCount {UserCount}, ScheduledCount {ScheduledCount}, WeekStart {WeekStart}, MonthStart {MonthStart}",
            users.Count,
            scheduled,
            weekStart,
            monthStart);
    }

    private static readonly TimeZoneInfo ShanghaiTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
}
