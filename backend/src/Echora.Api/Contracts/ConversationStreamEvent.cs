namespace Echora.Api.Contracts;

/// <summary>后端流式回答发送给 assistant-ui 适配层的领域事件。</summary>
public sealed record ConversationStreamEvent(
    string Kind,
    string? Text = null,
    string? ToolCallId = null,
    string? ToolName = null,
    long? AssistantTurnId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);
