using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>从一条聊天消息或一刻中整理出的明确事件。</summary>
[SugarTable("life_events")]
[SugarIndex("ix_life_events_user_conversation", nameof(UserId), OrderByType.Asc, nameof(ConversationId), OrderByType.Asc)]
[SugarIndex("ix_life_events_user_occurred_at", nameof(UserId), OrderByType.Asc, nameof(OccurredAt), OrderByType.Desc)]
public sealed class LifeEvent
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该事件的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>聊天来源的会话 ID；一刻来源为空。</summary>
    [SugarColumn(ColumnName = "conversation_id", IsNullable = true)]
    public long? ConversationId { get; set; }

    /// <summary>事件标题。</summary>
    [SugarColumn(ColumnName = "title", Length = 150)]
    public string Title { get; set; } = string.Empty;

    /// <summary>事件的事实描述。</summary>
    [SugarColumn(ColumnName = "summary", ColumnDataType = "text")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>由来源消息时间确定的发生时间。</summary>
    [SugarColumn(ColumnName = "occurred_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>已经可靠关联的人物 ID。</summary>
    [SugarColumn(ColumnName = "person_ids", ColumnDataType = "int8[]", IsArray = true)]
    public long[] PersonIds { get; set; } = [];

    /// <summary>已经可靠关联的地点 ID。</summary>
    [SugarColumn(ColumnName = "place_ids", ColumnDataType = "int8[]", IsArray = true)]
    public long[] PlaceIds { get; set; } = [];

    /// <summary>与事件直接相关的附件 ID。</summary>
    [SugarColumn(ColumnName = "attachment_ids", ColumnDataType = "int8[]", IsArray = true)]
    public long[] AttachmentIds { get; set; } = [];

    /// <summary>直接支持本事件的 User Message ID。</summary>
    [SugarColumn(ColumnName = "source_message_id", IsNullable = true)]
    public long? SourceMessageId { get; set; }

    /// <summary>直接支持本事件的一刻 ID。</summary>
    [SugarColumn(ColumnName = "source_moment_id", IsNullable = true)]
    public long? SourceMomentId { get; set; }

    /// <summary>事件首次创建时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>同一来源重试后的更新时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
