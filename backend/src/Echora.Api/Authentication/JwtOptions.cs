namespace Echora.Api.Authentication;

/// <summary>多用户 JWT 的签发和验证配置。</summary>
public sealed class JwtOptions
{
    /// <summary>配置文件中的节名称。</summary>
    public const string SectionName = "Jwt";

    /// <summary>签发者；必须与验证端完全一致。</summary>
    public string Issuer { get; init; } = "echora";

    /// <summary>允许使用令牌的 API audience。</summary>
    public string Audience { get; init; } = "echora-web";

    /// <summary>至少 32 字节的 HMAC signing key，只能来自本机忽略配置或部署 secret。</summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>访问令牌有效分钟数；本轮不实现 refresh token。</summary>
    public int LifetimeMinutes { get; init; } = 120;
}
