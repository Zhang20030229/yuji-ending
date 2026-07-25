namespace Echora.Api.Contracts;

/// <summary>聊天界面中的一条 User 或 Assistant 消息。</summary>
public sealed record ConversationTurnResponse(
    long Id,
    int Sequence,
    string Role,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ConversationPartResponse> Parts,
    string Status,
    string? ErrorMessage = null);
