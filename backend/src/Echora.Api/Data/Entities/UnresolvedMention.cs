using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>无法可靠绑定到现有人物或地点的一条待确认称呼。</summary>
[SugarTable("unresolved_mentions")]
[SugarIndex("ix_unresolved_mentions_user_status", nameof(UserId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
[SugarIndex("ix_unresolved_mentions_user_conversation", nameof(UserId), OrderByType.Asc, nameof(ConversationId), OrderByType.Asc)]
public sealed class UnresolvedMention
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该待确认项的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>聊天来源的会话 ID；一刻来源为空。</summary>
    [SugarColumn(ColumnName = "conversation_id", IsNullable = true)]
    public long? ConversationId { get; set; }

    /// <summary>待确认对象类型，只允许 Person 或 Place。</summary>
    [SugarColumn(ColumnName = "kind", Length = 20)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>用户称呼或图片中的对象描述。</summary>
    [SugarColumn(ColumnName = "mention", Length = 200)]
    public string Mention { get; set; } = string.Empty;

    /// <summary>无法可靠确定对象的原因。</summary>
    [SugarColumn(ColumnName = "reason", Length = 500)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>直接支持本待确认项的 User Message ID。</summary>
    [SugarColumn(ColumnName = "source_message_id", IsNullable = true)]
    public long? SourceMessageId { get; set; }

    /// <summary>直接支持本待确认项的一刻 ID。</summary>
    [SugarColumn(ColumnName = "source_moment_id", IsNullable = true)]
    public long? SourceMomentId { get; set; }

    /// <summary>与本待确认项有关的附件 ID。</summary>
    [SugarColumn(ColumnName = "attachment_ids", ColumnDataType = "int8[]", IsArray = true)]
    public long[] AttachmentIds { get; set; } = [];

    /// <summary>处理状态，只允许 Pending、Resolved 或 Ignored。</summary>
    [SugarColumn(ColumnName = "status", Length = 20)]
    public string Status { get; set; } = "Pending";

    /// <summary>用户选择或新建的人物/地点 ID。</summary>
    [SugarColumn(ColumnName = "resolved_entity_id", IsNullable = true)]
    public long? ResolvedEntityId { get; set; }

    /// <summary>用户完成处理的时间。</summary>
    [SugarColumn(ColumnName = "resolved_at", ColumnDataType = "timestamptz", IsNullable = true)]
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>待确认项创建时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
