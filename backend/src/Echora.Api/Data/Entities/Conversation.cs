using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>一份独立的产品会话资料集。</summary>
[SugarTable("conversations")]
[SugarIndex("ix_conversations_user_status", nameof(UserId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
[SugarIndex("ix_conversations_user_last_message", nameof(UserId), OrderByType.Asc, nameof(LastMessageAt), OrderByType.Desc)]
[SugarIndex("ix_conversations_external_space", nameof(UserId), OrderByType.Asc, nameof(Channel), OrderByType.Asc, nameof(ExternalSpaceId), OrderByType.Asc)]
[SugarIndex("ix_conversations_continued_from", nameof(ContinuedFromConversationId), OrderByType.Asc)]
public sealed class Conversation
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该会话的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>会话来源，只允许 Web 或 IMessage。</summary>
    [SugarColumn(ColumnName = "channel", Length = 20, DefaultValue = "'Web'")]
    public string Channel { get; set; } = "Web";

    /// <summary>外部渠道的稳定会话标识；Web 会话为空。</summary>
    [SugarColumn(ColumnName = "external_space_id", Length = 500, IsNullable = true)]
    public string? ExternalSpaceId { get; set; }

    /// <summary>会话及片段卡标题。</summary>
    [SugarColumn(ColumnName = "title", Length = 120)]
    public string Title { get; set; } = "新对话";

    /// <summary>用户是否已经手工修改标题。</summary>
    [SugarColumn(ColumnName = "is_title_edited")]
    public bool IsTitleEdited { get; set; }

    /// <summary>LifeRecordSubagent 生成的片段总结。</summary>
    [SugarColumn(ColumnName = "summary", ColumnDataType = "text", IsNullable = true)]
    public string? Summary { get; set; }

    /// <summary>会话状态，只允许 Current 或 Archived。</summary>
    [SugarColumn(ColumnName = "status", Length = 20)]
    public string Status { get; set; } = "Current";

    /// <summary>“继续聊”时引用的只读历史会话。</summary>
    [SugarColumn(ColumnName = "continued_from_conversation_id", IsNullable = true)]
    public long? ContinuedFromConversationId { get; set; }

    /// <summary>当前片段标题和总结已经覆盖到的最新 User Message 顺序。</summary>
    [SugarColumn(ColumnName = "summary_through_sequence")]
    public int SummaryThroughSequence { get; set; }

    /// <summary>最后一条消息的时间，用于排序和跨日判断。</summary>
    [SugarColumn(ColumnName = "last_message_at", ColumnDataType = "timestamptz", IsNullable = true)]
    public DateTimeOffset? LastMessageAt { get; set; }

    /// <summary>会话创建时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>会话最后更新时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
