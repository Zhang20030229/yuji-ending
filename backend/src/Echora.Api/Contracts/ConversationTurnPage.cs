namespace Echora.Api.Contracts;

/// <summary>按消息顺序游标读取的一页聊天记录。</summary>
public sealed record ConversationTurnPage(
    IReadOnlyList<ConversationTurnResponse> Items,
    int? NextBeforeSequence,
    bool HasMore);
