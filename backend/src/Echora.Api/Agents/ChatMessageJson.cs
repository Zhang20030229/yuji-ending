using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Echora.Api.Agents;

/// <summary>使用 Microsoft.Extensions.AI 官方多态配置持久化完整 ChatMessage。</summary>
public static class ChatMessageJson
{
    /// <summary>保存文本、推理、Function Call、Function Result 及其原始 CallId。</summary>
    public static string Serialize(ChatMessage message) =>
        JsonSerializer.Serialize(message, AIJsonUtilities.DefaultOptions);

    /// <summary>从数据库恢复 MAF 原生消息；无效 JSON 必须显式失败。</summary>
    public static ChatMessage Deserialize(string json) =>
        JsonSerializer.Deserialize<ChatMessage>(json, AIJsonUtilities.DefaultOptions)
        ?? throw new JsonException("聊天消息 JSON 为空。");
}
