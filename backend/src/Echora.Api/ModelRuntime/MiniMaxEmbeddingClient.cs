using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Echora.Api.ModelRuntime;

/// <summary>调用 MiniMax embo-01 的私有 embedding 协议；不是 OpenAI 兼容格式。</summary>
public sealed class MiniMaxEmbeddingClient(
    HttpClient http,
    EmbeddingOptions options,
    ILogger<MiniMaxEmbeddingClient> logger) : IMemoryEmbeddingClient
{
    /// <inheritdoc />
    public bool IsEnabled => options.IsEnabled;

    /// <inheritdoc />
    public string ModelId => options.ModelId;

    /// <inheritdoc />
    public int Dimensions => options.Dimensions;

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        EmbeddingPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (!options.IsEnabled)
            throw new ConversationRuntimeException("embedding.disabled", "embedding 模型未配置。");
        if (texts.Count == 0) return [];

        var results = new List<float[]>(texts.Count);
        foreach (var batch in Chunk(texts, options.BatchSize))
            results.AddRange(await EmbedBatchAsync(batch, purpose, cancellationToken));
        return results;
    }

    /// <summary>单批请求；MiniMax 用 texts/vectors 字段，且错误只体现在 base_resp。</summary>
    private async Task<float[][]> EmbedBatchAsync(
        IReadOnlyList<string> batch,
        EmbeddingPurpose purpose,
        CancellationToken cancellationToken)
    {
        var request = new EmbeddingRequest(
            options.ModelId,
            purpose == EmbeddingPurpose.Document ? "db" : "query",
            [.. batch]);

        using var response = await http.PostAsJsonAsync(BuildRequestUri(), request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ConversationRuntimeException(
                "embedding.request_failed",
                $"embedding 接口返回 HTTP {(int)response.StatusCode}。");

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken)
                      ?? throw new ConversationRuntimeException("embedding.invalid_response", "embedding 响应无法解析。");

        var status = payload.BaseResp?.StatusCode ?? 0;
        if (status != 0)
        {
            if (status == 1004)
                logger.LogError(
                    "Embedding auth rejected: StatusCode {StatusCode}, GroupIdConfigured {GroupIdConfigured}. 疑似缺少 GroupId 或 API Key 无效。",
                    status,
                    !string.IsNullOrWhiteSpace(options.GroupId));
            throw new ConversationRuntimeException(
                "embedding.provider_error",
                $"embedding 接口返回错误 {status}：{payload.BaseResp?.StatusMessage}");
        }

        var vectors = payload.Vectors;
        if (vectors is null || vectors.Length != batch.Count)
            throw new ConversationRuntimeException(
                "embedding.invalid_response",
                $"embedding 返回条数与请求不一致：请求 {batch.Count}，返回 {vectors?.Length ?? 0}。");
        foreach (var vector in vectors)
        {
            if (vector.Length != options.Dimensions)
                throw new ConversationRuntimeException(
                    "embedding.dimension_mismatch",
                    $"embedding 维度与配置不一致：期望 {options.Dimensions}，实际 {vector.Length}。");
        }
        return vectors;
    }

    /// <summary>GroupId 为空时不附加该查询参数，避免向服务端传空值。</summary>
    private string BuildRequestUri()
    {
        var path = $"{options.Endpoint.TrimEnd('/')}/embeddings";
        return string.IsNullOrWhiteSpace(options.GroupId)
            ? path
            : $"{path}?GroupId={Uri.EscapeDataString(options.GroupId)}";
    }

    /// <summary>按批大小切分，保持原始顺序。</summary>
    private static IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyList<string> texts, int size)
    {
        for (var index = 0; index < texts.Count; index += size)
            yield return [.. texts.Skip(index).Take(size)];
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("texts")] string[] Texts);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("vectors")] float[][]? Vectors,
        [property: JsonPropertyName("base_resp")] BaseResponse? BaseResp);

    private sealed record BaseResponse(
        [property: JsonPropertyName("status_code")] int StatusCode,
        [property: JsonPropertyName("status_msg")] string? StatusMessage);
}
