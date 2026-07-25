using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>会话内一条完整 MAF 消息及其发送状态。</summary>
[SugarTable("conversation_messages")]
[SugarIndex("ux_conversation_messages_sequence", nameof(ConversationId), OrderByType.Asc, nameof(Sequence), OrderByType.Asc, true)]
[SugarIndex("ux_conversation_messages_client_id", nameof(UserId), OrderByType.Asc, nameof(ClientMessageId), OrderByType.Asc, true)]
[SugarIndex("ix_conversation_messages_reply_to", nameof(ReplyToMessageId), OrderByType.Asc)]
[SugarIndex("ix_conversation_messages_user_created", nameof(UserId), OrderByType.Asc, nameof(CreatedAt), OrderByType.Desc)]
public sealed class ConversationMessage
{
    /// <summary>数据库自增主键，同时作为后台 Agent 的来源 ID。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该消息的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>所属会话 ID。</summary>
    [SugarColumn(ColumnName = "conversation_id")]
    public long ConversationId { get; set; }

    /// <summary>User Message 的客户端幂等 ID，其他角色为空。</summary>
    [SugarColumn(ColumnName = "client_message_id", Length = 80, IsNullable = true)]
    public string? ClientMessageId { get; set; }

    /// <summary>会话内严格递增的消息顺序。</summary>
    [SugarColumn(ColumnName = "sequence")]
    public int Sequence { get; set; }

    /// <summary>MAF 消息角色。</summary>
    [SugarColumn(ColumnName = "role", Length = 20)]
    public string Role { get; set; } = string.Empty;

    /// <summary>供界面和普通文本查询读取的文本内容。</summary>
    [SugarColumn(ColumnName = "text", ColumnDataType = "text", IsNullable = true)]
    public string? Text { get; set; }

    /// <summary>完整 MAF 内容，包含多模态、推理、Function Call 和 Function Result。</summary>
    [SugarColumn(ColumnName = "content_json", ColumnDataType = "text", IsNullable = true)]
    public string? ContentJson { get; set; }

    /// <summary>消息状态，只允许 Completed、Failed 或 Interrupted。</summary>
    [SugarColumn(ColumnName = "status", Length = 20)]
    public string Status { get; set; } = "Completed";

    /// <summary>Assistant 回答所对应的 User Message。</summary>
    [SugarColumn(ColumnName = "reply_to_message_id", IsNullable = true)]
    public long? ReplyToMessageId { get; set; }

    /// <summary>失败时可向用户展示的安全错误。</summary>
    [SugarColumn(ColumnName = "error_message", ColumnDataType = "text", IsNullable = true)]
    public string? ErrorMessage { get; set; }

    /// <summary>User Message 发送时授权取得的纬度。</summary>
    [SugarColumn(ColumnName = "latitude", IsNullable = true)]
    public double? Latitude { get; set; }

    /// <summary>User Message 发送时授权取得的经度。</summary>
    [SugarColumn(ColumnName = "longitude", IsNullable = true)]
    public double? Longitude { get; set; }

    /// <summary>浏览器定位误差半径，单位米。</summary>
    [SugarColumn(ColumnName = "accuracy_meters", IsNullable = true)]
    public double? AccuracyMeters { get; set; }

    /// <summary>设备位置的采集时间。</summary>
    [SugarColumn(ColumnName = "location_captured_at", ColumnDataType = "timestamptz", IsNullable = true)]
    public DateTimeOffset? LocationCapturedAt { get; set; }

    /// <summary>逆地理编码得到的省。</summary>
    [SugarColumn(ColumnName = "province", Length = 50, IsNullable = true)]
    public string? Province { get; set; }

    /// <summary>逆地理编码得到的市。</summary>
    [SugarColumn(ColumnName = "city", Length = 50, IsNullable = true)]
    public string? City { get; set; }

    /// <summary>定位服务解析出的附近地点或区域名称。</summary>
    [SugarColumn(ColumnName = "location_name", Length = 200, IsNullable = true)]
    public string? LocationName { get; set; }

    /// <summary>定位服务解析出的完整地址。</summary>
    [SugarColumn(ColumnName = "location_address", Length = 500, IsNullable = true)]
    public string? LocationAddress { get; set; }

    /// <summary>消息实际保存时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
