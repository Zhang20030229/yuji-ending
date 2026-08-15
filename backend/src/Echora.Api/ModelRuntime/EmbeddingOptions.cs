namespace Echora.Api.ModelRuntime;

/// <summary>记忆向量化使用的 embedding 模型配置。</summary>
public sealed class EmbeddingOptions
{
    /// <summary>配置文件中的节名称，挂在 AI 节之下。</summary>
    public const string SectionName = "AI:Embedding";

    /// <summary>embedding 接口基础地址。</summary>
    public string Endpoint { get; init; } = "https://api.minimaxi.com/v1";

    /// <summary>embedding 模型 ID。</summary>
    public string ModelId { get; init; } = "embo-01";

    /// <summary>embedding 服务 API Key；为空表示关闭向量检索。</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>MiniMax 国内端点所需的 GroupId；为空时不附加该查询参数。</summary>
    public string GroupId { get; init; } = string.Empty;

    /// <summary>向量维度；embo-01 固定为 1536。</summary>
    public int Dimensions { get; init; } = 1536;

    /// <summary>单次请求携带的文本条数。</summary>
    public int BatchSize { get; init; } = 16;

    /// <summary>配置完整且维度合法时才启用向量能力，否则整体降级为关键词检索。</summary>
    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ModelId)
        && Dimensions is >= 256 and <= 4096
        && BatchSize is > 0 and <= 64
        && Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";
}
