using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>一条聊天消息或一刻为人物留下的独立记录。</summary>
[SugarTable("person_records")]
[SugarIndex("ux_person_records_person_message", nameof(PersonId), OrderByType.Asc, nameof(SourceMessageId), OrderByType.Asc, true)]
[SugarIndex("ux_person_records_person_moment", nameof(PersonId), OrderByType.Asc, nameof(SourceMomentId), OrderByType.Asc, true)]
[SugarIndex("ix_person_records_user_conversation", nameof(UserId), OrderByType.Asc, nameof(ConversationId), OrderByType.Asc)]
public sealed class PersonRecord
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该人物记录的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>所属人物 ID。</summary>
    [SugarColumn(ColumnName = "person_id")]
    public long PersonId { get; set; }

    /// <summary>聊天来源的会话 ID；一刻来源为空。</summary>
    [SugarColumn(ColumnName = "conversation_id", IsNullable = true)]
    public long? ConversationId { get; set; }

    /// <summary>本会话与该人物有关的事实总结。</summary>
    [SugarColumn(ColumnName = "summary", ColumnDataType = "text")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>直接支持本记录的 User Message ID。</summary>
    [SugarColumn(ColumnName = "source_message_id", IsNullable = true)]
    public long? SourceMessageId { get; set; }

    /// <summary>直接支持本记录的一刻 ID。</summary>
    [SugarColumn(ColumnName = "source_moment_id", IsNullable = true)]
    public long? SourceMomentId { get; set; }

    /// <summary>已确认关联的图片附件 ID。</summary>
    [SugarColumn(ColumnName = "attachment_ids", ColumnDataType = "int8[]", IsArray = true)]
    public long[] AttachmentIds { get; set; } = [];

    /// <summary>记录首次创建时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>同一来源重试后的更新时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
