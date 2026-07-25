using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>情绪页面使用的日或月确定性投影。</summary>
[SugarTable("emotion_summaries")]
[SugarIndex("ux_emotion_summaries_user_period", nameof(UserId), OrderByType.Asc, nameof(PeriodType), OrderByType.Asc, nameof(PeriodStart), OrderByType.Asc, true)]
public sealed class EmotionSummary
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该情绪投影的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>投影类型，只允许 Day 或 Month。</summary>
    [SugarColumn(ColumnName = "period_type", Length = 10)]
    public string PeriodType { get; set; } = string.Empty;

    /// <summary>自然日或月份第一天。</summary>
    [SugarColumn(ColumnName = "period_start", ColumnDataType = "date")]
    public DateOnly PeriodStart { get; set; }

    /// <summary>根据已保存情绪记录拼装的事实总结。</summary>
    [SugarColumn(ColumnName = "summary", ColumnDataType = "text", IsNullable = true)]
    public string? Summary { get; set; }

    /// <summary>页面使用的家族计数、峰值、子类和趋势投影。</summary>
    [SugarColumn(ColumnName = "metrics_json", ColumnDataType = "text")]
    public string MetricsJson { get; set; } = "{}";

    /// <summary>投影最后重算时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
