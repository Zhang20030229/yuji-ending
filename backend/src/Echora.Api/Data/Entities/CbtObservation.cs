using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>从一条用户原话中整理出的情境、想法、身体反应、行为和直接结果。</summary>
[SugarTable("cbt_observations")]
[SugarIndex("ux_cbt_observations_user_message", nameof(UserId), OrderByType.Asc, nameof(SourceMessageId), OrderByType.Asc, true)]
[SugarIndex("ux_cbt_observations_user_moment", nameof(UserId), OrderByType.Asc, nameof(SourceMomentId), OrderByType.Asc, true)]
[SugarIndex("ix_cbt_observations_user_occurred_at", nameof(UserId), OrderByType.Asc, nameof(OccurredAt), OrderByType.Desc)]
public sealed class CbtObservation
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该观察的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>聊天来源的会话 ID；一刻来源为空。</summary>
    [SugarColumn(ColumnName = "conversation_id", IsNullable = true)]
    public long? ConversationId { get; set; }

    /// <summary>原话中客观发生的情境。</summary>
    [SugarColumn(ColumnName = "situation", ColumnDataType = "text")]
    public string Situation { get; set; } = string.Empty;

    /// <summary>原话中明确表达的即时想法；没有时为空。</summary>
    [SugarColumn(ColumnName = "automatic_thought", ColumnDataType = "text", IsNullable = true)]
    public string? AutomaticThought { get; set; }

    /// <summary>原话中明确表达的身体感受；没有时为空。</summary>
    [SugarColumn(ColumnName = "body_sensation", ColumnDataType = "text", IsNullable = true)]
    public string? BodySensation { get; set; }

    /// <summary>原话中明确表达的做法或回避；没有时为空。</summary>
    [SugarColumn(ColumnName = "behavior", ColumnDataType = "text", IsNullable = true)]
    public string? Behavior { get; set; }

    /// <summary>原话中明确表达的直接结果；没有时为空。</summary>
    [SugarColumn(ColumnName = "immediate_outcome", ColumnDataType = "text", IsNullable = true)]
    public string? ImmediateOutcome { get; set; }

    /// <summary>承载原话的消息或一刻发生时间。</summary>
    [SugarColumn(ColumnName = "occurred_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>直接支持本观察的 User Message ID。</summary>
    [SugarColumn(ColumnName = "source_message_id", IsNullable = true)]
    public long? SourceMessageId { get; set; }

    /// <summary>直接支持本观察的一刻 ID。</summary>
    [SugarColumn(ColumnName = "source_moment_id", IsNullable = true)]
    public long? SourceMomentId { get; set; }

    /// <summary>观察首次创建时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>用户编辑或同源重跑后的更新时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
