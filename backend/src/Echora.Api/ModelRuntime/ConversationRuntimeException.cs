namespace Echora.Api.ModelRuntime;

/// <summary>把模型供应商或运行框架错误归一化为安全产品错误。</summary>
public sealed class ConversationRuntimeException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>客户端可以稳定判断且不暴露供应商响应正文的错误码。</summary>
    public string Code { get; } = code;
}
