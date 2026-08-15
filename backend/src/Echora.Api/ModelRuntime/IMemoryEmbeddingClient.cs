namespace Echora.Api.ModelRuntime;

/// <summary>embedding 的用途；MiniMax 对入库文本与检索查询使用不同的 type。</summary>
public enum EmbeddingPurpose
{
    /// <summary>入库的记忆文本，对应 MiniMax 的 type=db。</summary>
    Document,

    /// <summary>检索用的查询词，对应 MiniMax 的 type=query。</summary>
    Query,
}

/// <summary>批量文本向量化。</summary>
public interface IMemoryEmbeddingClient
{
    /// <summary>当前配置是否可用；不可用时调用方必须降级为关键词检索。</summary>
    bool IsEnabled { get; }

    /// <summary>写入 memory_embeddings 的模型标识，用于识别过期向量。</summary>
    string ModelId { get; }

    /// <summary>向量维度。</summary>
    int Dimensions { get; }

    /// <summary>按输入顺序返回等长的向量列表；任一环节失败直接抛出。</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        EmbeddingPurpose purpose,
        CancellationToken cancellationToken = default);
}
