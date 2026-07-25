namespace Echora.Api.Contracts;

/// <summary>会话列表中的一项。</summary>
public sealed record ConversationSessionResponse(
    long Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Status,
    string Channel,
    bool IsWritable);
