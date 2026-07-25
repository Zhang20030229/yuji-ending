using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>一个时间点由用户原话支持的一种情绪。</summary>
[SugarTable("emotion_records")]
[SugarIndex("ix_emotion_records_user_conversation", nameof(UserId), OrderByType.Asc, nameof(ConversationId), OrderByType.Asc)]
[SugarIndex("ix_emotion_records_user_occurred_at", nameof(UserId), OrderByType.Asc, nameof(OccurredAt), OrderByType.Desc)]
[SugarIndex("ix_emotion_records_user_family", nameof(UserId), OrderByType.Asc, nameof(Family), OrderByType.Asc)]
public sealed class EmotionRecord
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该情绪记录的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>聊天来源的会话 ID；一刻来源为空。</summary>
    [SugarColumn(ColumnName = "conversation_id", IsNullable = true)]
    public long? ConversationId { get; set; }

    /// <summary>11 个固定情绪家族之一。</summary>
    [SugarColumn(ColumnName = "family", Length = 20)]
    public string Family { get; set; } = string.Empty;

    /// <summary>所属家族中的固定情绪子类。</summary>
    [SugarColumn(ColumnName = "subtype", Length = 20)]
    public string Subtype { get; set; } = string.Empty;

    /// <summary>本次情绪表达强度，范围 1～5。</summary>
    [SugarColumn(ColumnName = "intensity")]
    public short Intensity { get; set; }

    /// <summary>本次情绪及其直接语境。</summary>
    [SugarColumn(ColumnName = "summary", ColumnDataType = "text")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>承载原话的 User Message 实际发送时间。</summary>
    [SugarColumn(ColumnName = "occurred_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>直接支持本情绪的 User Message ID。</summary>
    [SugarColumn(ColumnName = "source_message_id", IsNullable = true)]
    public long? SourceMessageId { get; set; }

    /// <summary>直接支持本情绪的一刻 ID。</summary>
    [SugarColumn(ColumnName = "source_moment_id", IsNullable = true)]
    public long? SourceMomentId { get; set; }

    /// <summary>情绪首次创建时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>同一来源重试后的更新时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
