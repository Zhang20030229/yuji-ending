using System.ClientModel;
using System.Net.Sockets;
namespace Echora.Api.ModelRuntime;

/// <summary>把 Provider SDK 的直接或包装异常归一化为稳定、安全且不误导的产品错误。</summary>
internal static class ModelProviderErrorMapper
{
    /// <summary>把 Provider 异常映射成稳定且不泄露私有正文的领域错误。</summary>
    public static ConversationRuntimeException? TryMap(Exception exception, string rejectedMessage)
    {
        var errors = Flatten(exception).ToArray();
        var clientError = errors.OfType<ClientResultException>().FirstOrDefault();
        if (errors.Any(error => error is HttpRequestException or SocketException)
            || clientError?.Status == 0)
            return new ConversationRuntimeException("model.unreachable", "主模型当前无法连接。", exception);
        if (clientError is null) return null;

        var (code, message) = clientError.Status switch
        {
            401 or 403 => ("model.auth_failed", "主模型 API Key 无效或没有权限。"),
            404 => ("model.model_not_found", "主模型 ID 或地址不存在。"),
            408 => ("model.timeout", "主模型请求超时。"),
            429 => ("model.rate_limited", "主模型请求过于频繁，请稍后重试。"),
            >= 500 => ("model.unreachable", "主模型服务暂时不可用。"),
            _ => ("model.invalid_response", rejectedMessage),
        };
        return new ConversationRuntimeException(code, message, exception);
    }

    /// <summary>按外到内枚举异常链，供供应商错误码映射使用。</summary>
    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        yield return exception;
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
                foreach (var nested in Flatten(inner))
                    yield return nested;
        }
        else if (exception.InnerException is not null)
        {
            foreach (var nested in Flatten(exception.InnerException))
                yield return nested;
        }
    }
}
