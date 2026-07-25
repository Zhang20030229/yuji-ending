namespace Echora.Api.Contracts;

/// <summary>创建新会话时可选引用一个只读历史会话。</summary>
public sealed record CreateConversationRequest(long? ContinuedFromConversationId = null);
