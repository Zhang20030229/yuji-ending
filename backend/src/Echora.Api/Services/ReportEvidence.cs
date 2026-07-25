namespace Echora.Api.Services;

/// <summary>报告分区保存的一条真实用户原话与相关图片快照。</summary>
public sealed class ReportEvidence
{
    /// <summary>只在本次报告中使用的临时引用，例如 E1。</summary>
    public string Ref { get; set; } = string.Empty;

    /// <summary>原话实际发生时间。</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>来源类型：Conversation 或 Moment。</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>完整用户原话。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>聊天来源的真实消息 ID，只供服务端查图。</summary>
    public long? MessageId { get; set; }

    /// <summary>一刻来源 ID，只供服务端校验。</summary>
    public long? MomentId { get; set; }

    /// <summary>本分区确认相关的图片附件 ID。</summary>
    public long[] AttachmentIds { get; set; } = [];

    /// <summary>传给报告智能体的既有客观图片说明。</summary>
    public string[] AttachmentDescriptions { get; set; } = [];
}
