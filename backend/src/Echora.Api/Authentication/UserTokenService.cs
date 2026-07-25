using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Echora.Api.Contracts;
using Echora.Api.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Echora.Api.Authentication;

/// <summary>为已经通过身份验证的用户签发 JWT。</summary>
public sealed class UserTokenService(JwtOptions options)
{
    /// <summary>签发包含用户 ID 与 TokenVersion 的访问令牌。</summary>
    public AuthTokenResponse Create(UserAccount user)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(options.LifetimeMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim("token_version", user.TokenVersion.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        var token = new JwtSecurityToken(
            options.Issuer,
            options.Audience,
            claims,
            issuedAt.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);

        return new AuthTokenResponse
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = expiresAt,
        };
    }
}
