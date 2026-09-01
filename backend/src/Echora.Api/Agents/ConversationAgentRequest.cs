using Echora.Api.Entities;
using Microsoft.Extensions.AI;

namespace Echora.Api.Agents;

/// <summary>ConversationAgent 一次流式运行所需的完整、不可变输入。</summary>
public sealed record ConversationAgentRequest(
    UserAccount User,
    IReadOnlyList<ChatMessage> Messages,
    bool TextOnly = false,
    DateTimeOffset? ReferenceTime = null,
    bool SessionOpening = false);
