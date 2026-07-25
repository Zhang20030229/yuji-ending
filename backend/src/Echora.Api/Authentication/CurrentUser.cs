using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Echora.Api.Authentication;

/// <summary>从已验证 JWT 中读取当前用户标识。</summary>
public static class CurrentUser
{
    /// <summary>返回当前用户 ID；缺少有效 subject 时拒绝请求。</summary>
    public static long GetRequiredId(this ClaimsPrincipal user)
    {
        var subject = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return long.TryParse(subject, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("当前登录凭据无效。");
    }
}
