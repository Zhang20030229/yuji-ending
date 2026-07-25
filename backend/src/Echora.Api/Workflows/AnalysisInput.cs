using Microsoft.Extensions.AI;

namespace Echora.Api.Workflows;

/// <summary>三个后台 Subagent 共用的一条消息或一刻资料。</summary>
public sealed record AnalysisInput(
    long AnalysisRunId,
    long UserId,
    string UserDisplayName,
    string AiName,
    long? ConversationId,
    long? TargetMessageId,
    long? MomentId,
    int TargetSequence,
    ChatMessage[] Messages,
    long[] AttachmentIds,
    DateTimeOffset OccurredAt,
    string[] Branches)
{
    /// <summary>当前分析来源是否为一刻。</summary>
    public bool IsMoment => MomentId.HasValue;

    /// <summary>一刻是否包含用户主动填写的文字；纯图片不能证明人物、地点、事件、认识或情绪。</summary>
    public bool HasExplicitText { get; init; } = true;

    /// <summary>当前用户亲自输入的原文；不包含图片说明、设备位置或历史上下文。</summary>
    public string ExplicitText { get; init; } = string.Empty;
}
