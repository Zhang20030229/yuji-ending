using Microsoft.Extensions.AI;

namespace Echora.Api.ModelRuntime;

/// <summary>隔离 OpenAI SDK 的实验 API，使 Agent 只依赖稳定的聊天客户端契约。</summary>
public interface IModelChatClientFactory
{
    /// <summary>按已验证配置创建一次 MAF 使用的聊天客户端。</summary>
    IChatClient Create(
        ModelCapabilityConfigurationSnapshot configuration,
        ReasoningEffort reasoningEffort = ReasoningEffort.High,
        TimeSpan? networkTimeout = null);
}
