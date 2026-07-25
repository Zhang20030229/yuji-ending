namespace Echora.Api.Contracts;

/// <summary>用户首次填写或以后修改的最小个人资料。</summary>
public sealed class ProfileRequest
{
    /// <summary>AI 对用户使用的称呼。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>用户性别，只允许 Male 或 Female。</summary>
    public string Gender { get; init; } = string.Empty;

    /// <summary>用户出生年份。</summary>
    public int BirthYear { get; init; }

    /// <summary>用户出生月份，范围 1～12。</summary>
    public int BirthMonth { get; init; }

    /// <summary>用户给 AI 伙伴取的名字。</summary>
    public string AiName { get; init; } = string.Empty;
}
