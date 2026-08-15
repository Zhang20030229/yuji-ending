namespace Echora.Api.ModelRuntime;

/// <summary>按端点主机名识别的模型供应商；决定推理参数与私有兼容补丁。</summary>
public enum ModelVendor
{
    /// <summary>标准 OpenAI Chat Completions 行为，使用 reasoning_effort。</summary>
    OpenAiCompatible,

    /// <summary>小米 MiMo：使用私有 thinking.type，并需要 SSE 与工具调用兼容补丁。</summary>
    Mimo,

    /// <summary>MiniMax：OpenAI 兼容协议，但使用私有 thinking.type。</summary>
    MiniMax,
}

/// <summary>从端点主机名解析供应商。</summary>
public static class ModelVendorResolver
{
    /// <summary>只依据主机名判断，避免在多处重复散落的 bool 判定。</summary>
    public static ModelVendor Resolve(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var host = endpoint.Host;
        if (host.EndsWith("xiaomimimo.com", StringComparison.OrdinalIgnoreCase))
            return ModelVendor.Mimo;
        if (host.EndsWith("minimaxi.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("minimax.chat", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("minimax.io", StringComparison.OrdinalIgnoreCase))
            return ModelVendor.MiniMax;
        return ModelVendor.OpenAiCompatible;
    }
}
