using Microsoft.Agents.AI;

namespace Echora.Api.Agents;

/// <summary>ConversationAgent 的可见增量或最终完整响应。</summary>
public sealed record ConversationAgentUpdate(
    string Kind,
    string? Text = null,
    string? ToolCallId = null,
    string? ToolName = null,
    AgentResponse? Response = null);
