namespace Echora.Api.Contracts;

/// <summary>JWT 登录成功回执。</summary>
public sealed class AuthTokenResponse
{
    /// <summary>后续 API 请求使用的 Bearer Token。</summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>Token 的 UTC 失效时间。</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}
