namespace Echora.Api.Contracts;

/// <summary>用户注册请求。</summary>
public sealed class RegisterRequest
{
    /// <summary>全局唯一用户名，只允许中文、英文字母、数字和下划线。</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>6～18 位登录密码。</summary>
    public string Password { get; init; } = string.Empty;
}
