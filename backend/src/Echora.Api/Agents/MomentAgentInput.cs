using Microsoft.Extensions.AI;

namespace Echora.Api.Agents;

/// <summary>MomentAgent 使用的当前用户与单条一刻多模态输入。</summary>
public sealed record MomentAgentInput(long UserId, ChatMessage Message);
