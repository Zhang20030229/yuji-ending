using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>同一用户在一个日期范围内唯一的一份心迹报告包。</summary>
[SugarTable("report_packs")]
[SugarIndex(
    "ux_report_packs_user_range",
    nameof(UserId), OrderByType.Asc,
    nameof(StartDate), OrderByType.Asc,
    nameof(EndDate), OrderByType.Asc,
    true)]
[SugarIndex("ix_report_packs_user_updated", nameof(UserId), OrderByType.Asc, nameof(UpdatedAt), OrderByType.Desc)]
public sealed class ReportPack
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该报告的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>周期类型：Weekly、Monthly 或 CustomRange。</summary>
    [SugarColumn(ColumnName = "period_type", Length = 20)]
    public string PeriodType { get; set; } = string.Empty;

    /// <summary>报告范围第一天，包含当天。</summary>
    [SugarColumn(ColumnName = "start_date", ColumnDataType = "date")]
    public DateOnly StartDate { get; set; }

    /// <summary>报告范围最后一天，包含当天。</summary>
    [SugarColumn(ColumnName = "end_date", ColumnDataType = "date")]
    public DateOnly EndDate { get; set; }

    /// <summary>触发方式：Manual 或 Automatic。</summary>
    [SugarColumn(ColumnName = "trigger_type", Length = 20)]
    public string TriggerType { get; set; } = "Manual";

    /// <summary>报告包状态。</summary>
    [SugarColumn(ColumnName = "status", Length = 24)]
    public string Status { get; set; } = "Pending";

    /// <summary>综合报告智能体生成的结构化内容；未生成时为空。</summary>
    [SugarColumn(ColumnName = "overall_content_json", ColumnDataType = "text", IsNullable = true)]
    public string? OverallContentJson { get; set; }

    /// <summary>整体或综合报告最近一次安全错误摘要。</summary>
    [SugarColumn(ColumnName = "error_summary", ColumnDataType = "text", IsNullable = true)]
    public string? ErrorSummary { get; set; }

    /// <summary>报告任务取得执行权的次数。</summary>
    [SugarColumn(ColumnName = "attempt_count")]
    public int AttemptCount { get; set; }

    /// <summary>最近一次完整报告任务耗时，单位毫秒。</summary>
    [SugarColumn(ColumnName = "duration_ms", IsNullable = true)]
    public long? DurationMs { get; set; }

    /// <summary>当前后台任务编号。</summary>
    [SugarColumn(ColumnName = "hangfire_job_id", Length = 100, IsNullable = true)]
    public string? HangfireJobId { get; set; }

    /// <summary>报告包首次建立时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>报告包最近一次状态变化时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
