namespace Echora.Api.Contracts;

/// <summary>当前登录用户可在前端使用的资料。</summary>
public sealed class UserResponse
{
    /// <summary>登录用户名。</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>AI 对用户使用的称呼。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>用户性别。</summary>
    public string Gender { get; init; } = string.Empty;

    /// <summary>用户出生年份；未完成资料时为空。</summary>
    public int? BirthYear { get; init; }

    /// <summary>用户出生月份；未完成资料时为空。</summary>
    public int? BirthMonth { get; init; }

    /// <summary>根据当前年月和出生年月自动换算的年龄。</summary>
    public int? Age { get; init; }

    /// <summary>用户给 AI 伙伴取的名字。</summary>
    public string AiName { get; init; } = string.Empty;

    /// <summary>是否已完成首次个人资料。</summary>
    public bool ProfileCompleted { get; init; }
}
