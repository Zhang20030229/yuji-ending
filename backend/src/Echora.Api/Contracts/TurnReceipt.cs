namespace Echora.Api.Contracts;

/// <summary>User Message 可靠落库后的回执。</summary>
public sealed record TurnReceipt(
    long TurnId,
    long ConversationId,
    DateTimeOffset SavedAt,
    bool IsDuplicate);
