namespace Echora.Api.ModelRuntime;

/// <summary>服务端固定的对话模型配置。</summary>
public sealed class AiOptions
{
    /// <summary>配置文件中的节名称。</summary>
    public const string SectionName = "AI";

    /// <summary>OpenAI Chat Completions 兼容地址。</summary>
    public string Endpoint { get; init; } = "https://api.minimaxi.com/v1";

    /// <summary>固定模型 ID。</summary>
    public string ModelId { get; init; } = "MiniMax-M3";

    /// <summary>服务端模型 API Key。</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>是否为面向用户的回答开启可见思考。</summary>
    public bool ReasoningEnabled { get; init; } = true;

    /// <summary>转换成模型客户端的一次不可变配置。</summary>
    public ModelCapabilityConfigurationSnapshot ToSnapshot() => new(
        "openai_chat_completions",
        Endpoint,
        ModelId,
        ApiKey,
        1);
}
