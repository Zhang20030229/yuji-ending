using System.ComponentModel;
using Echora.Api.Contracts;

namespace Echora.Api.Plugins;

/// <summary>为 ConversationAgent 提供确定性的上海时间。</summary>
public sealed class TimePlugin(DateTimeOffset? referenceTime = null)
{
    /// <summary>获取当前日期、时间和星期。用户询问现在的时间，或回答必须依赖今天、昨天等相对日期时调用。</summary>
    [DisplayName("get_current_time")]
    [Description("获取当前日期、时间和星期。用户询问现在的时间，或回答必须依赖今天、昨天等相对日期时调用。")]
    public Task<CurrentTimeResult> GetCurrentTimeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        // 普通会话使用当前时间；演示历史会话使用消息发生时间，保证相对日期解释真实可复现。
        var now = TimeZoneInfo.ConvertTime(referenceTime ?? DateTimeOffset.UtcNow, zone);
        var week = new[] { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
        return Task.FromResult(new CurrentTimeResult
        {
            LocalTime = now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            Date = now.ToString("yyyy-MM-dd"),
            DayOfWeek = week[(int)now.DayOfWeek],
            UtcOffset = now.ToString("zzz"),
        });
    }
}
