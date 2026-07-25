namespace Echora.Api.Contracts;

/// <summary>时间 Tool 的固定返回结构。</summary>
public sealed class CurrentTimeResult
{
    /// <summary>Asia/Shanghai 当前时间。</summary>
    public string LocalTime { get; init; } = string.Empty;

    /// <summary>本地日期。</summary>
    public string Date { get; init; } = string.Empty;

    /// <summary>中文星期。</summary>
    public string DayOfWeek { get; init; } = string.Empty;

    /// <summary>UTC 偏移。</summary>
    public string UtcOffset { get; init; } = string.Empty;
}
