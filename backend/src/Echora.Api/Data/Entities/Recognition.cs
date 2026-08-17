using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>从一条聊天消息或一刻原话中整理出的用户认识。</summary>
[SugarTable("recognitions")]
[SugarIndex("ix_recognitions_user_conversation", nameof(UserId), OrderByType.Asc, nameof(ConversationId), OrderByType.Asc)]
[SugarIndex("ix_recognitions_user_category", nameof(UserId), OrderByType.Asc, nameof(Category), OrderByType.Asc)]
[SugarIndex("ix_recognitions_user_rejected", nameof(UserId), OrderByType.Asc, nameof(RejectedAt), OrderByType.Asc)]
public sealed class Recognition
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该认识记录的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>聊天来源的会话 ID；一刻来源为空。</summary>
    [SugarColumn(ColumnName = "conversation_id", IsNullable = true)]
    public long? ConversationId { get; set; }

    /// <summary>九个固定认识分类之一。</summary>
    [SugarColumn(ColumnName = "category", Length = 20)]
    public string Category { get; set; } = string.Empty;

    /// <summary>可独立阅读的一条用户认识。</summary>
    [SugarColumn(ColumnName = "content", ColumnDataType = "text")]
    public string Content { get; set; } = string.Empty;

    /// <summary>用于分类查询的 1～5 个关键词。</summary>
    [SugarColumn(ColumnName = "keywords", ColumnDataType = "text[]", IsArray = true)]
    public string[] Keywords { get; set; } = [];

    /// <summary>直接支持本认识的 User Message ID。</summary>
    [SugarColumn(ColumnName = "source_message_id", IsNullable = true)]
    public long? SourceMessageId { get; set; }

    /// <summary>直接支持本认识的一刻 ID。</summary>
    [SugarColumn(ColumnName = "source_moment_id", IsNullable = true)]
    public long? SourceMomentId { get; set; }

    /// <summary>认识首次创建时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>同一来源重试后的更新时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>用户驳回这条归纳的时间；为空表示未被驳回。</summary>
    [SugarColumn(ColumnName = "rejected_at", ColumnDataType = "timestamptz", IsNullable = true)]
    public DateTimeOffset? RejectedAt { get; set; }

    /// <summary>用户驳回时可选填写的说明，会作为负面清单的一部分提供给归纳模型。</summary>
    [SugarColumn(ColumnName = "rejection_note", ColumnDataType = "text", IsNullable = true)]
    public string? RejectionNote { get; set; }
}
