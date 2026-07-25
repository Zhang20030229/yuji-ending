using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>心迹报告中的一个生活、情绪、人际或认识分区。</summary>
[SugarTable("report_sections")]
[SugarIndex(
    "ux_report_sections_pack_kind",
    nameof(ReportPackId), OrderByType.Asc,
    nameof(Kind), OrderByType.Asc,
    true)]
[SugarIndex("ix_report_sections_user_status", nameof(UserId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
public sealed class ReportSection
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该分区的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>所属报告包。</summary>
    [SugarColumn(ColumnName = "report_pack_id")]
    public long ReportPackId { get; set; }

    /// <summary>分区类型：Life、Emotion、Relationship 或 Recognition。</summary>
    [SugarColumn(ColumnName = "kind", Length = 20)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>分区状态。</summary>
    [SugarColumn(ColumnName = "status", Length = 24)]
    public string Status { get; set; } = "Pending";

    /// <summary>由固定代码计算的确定性指标。</summary>
    [SugarColumn(ColumnName = "metrics_json", ColumnDataType = "text")]
    public string MetricsJson { get; set; } = "{}";

    /// <summary>报告子智能体生成的结构化内容。</summary>
    [SugarColumn(ColumnName = "content_json", ColumnDataType = "text", IsNullable = true)]
    public string? ContentJson { get; set; }

    /// <summary>本分区可引用的完整原话与相关图片快照。</summary>
    [SugarColumn(ColumnName = "evidence_json", ColumnDataType = "text")]
    public string EvidenceJson { get; set; } = "[]";

    /// <summary>最近一次安全错误摘要。</summary>
    [SugarColumn(ColumnName = "error_summary", ColumnDataType = "text", IsNullable = true)]
    public string? ErrorSummary { get; set; }

    /// <summary>该分区实际调用智能体的次数。</summary>
    [SugarColumn(ColumnName = "attempt_count")]
    public int AttemptCount { get; set; }

    /// <summary>该分区最近一次执行耗时，单位毫秒。</summary>
    [SugarColumn(ColumnName = "duration_ms", IsNullable = true)]
    public long? DurationMs { get; set; }

    /// <summary>分区首次建立时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>分区最近一次状态变化时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
