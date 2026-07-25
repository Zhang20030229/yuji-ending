namespace Echora.Api.Contracts;

/// <summary>用户使用账号密码登录的请求。</summary>
public sealed class LoginRequest
{
    /// <summary>登录账号。</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>登录密码。</summary>
    public string Password { get; init; } = string.Empty;
}
