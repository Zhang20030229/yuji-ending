namespace Echora.Api.Contracts;

/// <summary>当前用户修改登录密码的请求。</summary>
public sealed class ChangePasswordRequest
{
    /// <summary>当前密码，用于重新验证身份。</summary>
    public string CurrentPassword { get; init; } = string.Empty;

    /// <summary>新的 6～18 位密码。</summary>
    public string NewPassword { get; init; } = string.Empty;
}
