using System.Text;
using System.Text.RegularExpressions;
using Echora.Api.Contracts;
using Echora.Api.Entities;
using Echora.Api.BackgroundJobs;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>处理多用户注册、登录、资料和密码修改。</summary>
public sealed partial class AuthService(
    ISqlSugarClient db,
    IPasswordHasher<UserAccount> passwordHasher,
    IMemoryCache cache,
    IBackgroundJobClient? backgroundJobs = null)
{
    private static readonly TimeZoneInfo ChinaTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    /// <summary>校验账号信息并创建一个新用户。</summary>
    public async Task<UserAccount> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var normalizedUsername = NormalizeUsername(username);
        ValidatePassword(request.Password);

        if (!UsernamePattern().IsMatch(username))
            throw new ArgumentException("用户名必须为 3～20 位中文、英文字母、数字或下划线。");
        if (await db.Queryable<UserAccount>()
                .Where(item => item.NormalizedUsername == normalizedUsername)
                .AnyAsync(cancellationToken))
            throw new InvalidOperationException("用户名已存在。");

        var user = new UserAccount
        {
            Username = username,
            NormalizedUsername = normalizedUsername,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        user.Id = await db.Insertable(user).ExecuteReturnBigIdentityAsync(cancellationToken);
        cache.Set(TokenCacheKey(user.Id, user.TokenVersion), true, TimeSpan.FromSeconds(30));
        return user;
    }

    /// <summary>验证用户名和密码；失败时返回空。</summary>
    public async Task<UserAccount?> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedUsername = NormalizeUsername(request.Username.Trim());
        var user = await db.Queryable<UserAccount>()
            .Where(item => item.NormalizedUsername == normalizedUsername)
            .FirstAsync(cancellationToken);
        if (user is null) return null;

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.Updateable(user).ExecuteCommandAsync(cancellationToken);
        }
        cache.Set(TokenCacheKey(user.Id, user.TokenVersion), true, TimeSpan.FromSeconds(30));
        return user;
    }

    /// <summary>读取指定用户；所有调用者必须传入当前 JWT 的用户 ID。</summary>
    public async Task<UserAccount?> GetUserAsync(long userId, CancellationToken cancellationToken)
    {
        // SqlSugar 的 FirstAsync 在没有记录时返回 null，但其泛型签名尚未标注可空。
        return await db.Queryable<UserAccount>()
            .Where(item => item.Id == userId)
            .FirstAsync(cancellationToken);
    }

    /// <summary>验证 JWT 中的用户和版本；短暂缓存成功结果，避免每个图片和 API 请求都重复查询用户表。</summary>
    public async Task<bool> IsTokenValidAsync(
        long userId,
        int tokenVersion,
        CancellationToken cancellationToken)
    {
        var key = TokenCacheKey(userId, tokenVersion);
        if (cache.TryGetValue(key, out bool valid)) return valid;

        valid = await db.Queryable<UserAccount>()
            .AnyAsync(item => item.Id == userId && item.TokenVersion == tokenVersion, cancellationToken);
        if (valid) cache.Set(key, true, TimeSpan.FromSeconds(30));
        return valid;
    }

    /// <summary>保存首次或后续修改的最小个人资料。</summary>
    public async Task<UserAccount> SaveProfileAsync(
        long userId,
        ProfileRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetRequiredUserAsync(userId, cancellationToken);
        var displayName = request.DisplayName.Trim();
        var aiName = request.AiName.Trim();
        if (displayName.Length is < 1 or > 50) throw new ArgumentException("请填写有效的称呼。");
        if (aiName.Length is < 1 or > 50) throw new ArgumentException("请填写有效的 AI 名字。");
        if (request.Gender is not ("Male" or "Female")) throw new ArgumentException("性别只能是 Male 或 Female。");
        ValidateBirthMonth(request.BirthYear, request.BirthMonth);

        user.DisplayName = displayName;
        user.Gender = request.Gender;
        user.BirthYear = request.BirthYear;
        user.BirthMonth = request.BirthMonth;
        user.AiName = aiName;
        user.ProfileCompleted = true;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(user).ExecuteCommandAsync(cancellationToken);
        return user;
    }

    /// <summary>验证当前密码并设置新密码，同时使旧 JWT 失效。</summary>
    public async Task<UserAccount> ChangePasswordAsync(
        long userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePassword(request.NewPassword);
        var user = await GetRequiredUserAsync(userId, cancellationToken);
        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword)
            == PasswordVerificationResult.Failed)
            throw new ArgumentException("当前密码错误。");

        var oldTokenVersion = user.TokenVersion;
        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.TokenVersion++;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(user).ExecuteCommandAsync(cancellationToken);
        cache.Remove(TokenCacheKey(userId, oldTokenVersion));
        return user;
    }

    /// <summary>先持久投递清理任务，再立即删除账号行使现有 JWT 失效。</summary>
    public async Task<bool> DeleteAccountAsync(long userId, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        if (user is null)
            return false;
        if (backgroundJobs is null)
            throw new InvalidOperationException("后台清理服务未启用，暂时不能删除账号。");

        backgroundJobs.Enqueue<AccountCleanupJob>(job => job.ExecuteAsync(userId));
        await db.Deleteable<UserAccount>()
            .Where(item => item.Id == userId)
            .ExecuteCommandAsync(cancellationToken);
        cache.Remove(TokenCacheKey(userId, user.TokenVersion));
        return true;
    }

    /// <summary>按确认规则规范化用户名，中文保持不变，英文字母忽略大小写。</summary>
    public static string NormalizeUsername(string username) =>
        username.Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    /// <summary>令牌缓存只保存不可逆的数值标识，不保存 JWT 或用户资料。</summary>
    private static string TokenCacheKey(long userId, int tokenVersion) =>
        $"jwt-user:{userId}:{tokenVersion}";

    /// <summary>按中国时区的当前年月换算年龄；未填写出生年月时返回空。</summary>
    public static int? CalculateAge(int? birthYear, int? birthMonth)
    {
        if (birthYear is null || birthMonth is null) return null;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            ChinaTimeZone).Date);
        return today.Year - birthYear.Value - (today.Month < birthMonth.Value ? 1 : 0);
    }

    /// <summary>确保出生年月真实存在、不是未来且年龄不超过 120 岁。</summary>
    private static void ValidateBirthMonth(int birthYear, int birthMonth)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            ChinaTimeZone).Date);
        if (birthMonth is < 1 or > 12
            || birthYear < today.Year - 120
            || birthYear > today.Year
            || (birthYear == today.Year && birthMonth > today.Month))
            throw new ArgumentException("请填写有效的出生年月。");
    }

    /// <summary>验证 6～18 位密码，不增加字符组合限制。</summary>
    private static void ValidatePassword(string password)
    {
        if (password.Length is < 6 or > 18)
            throw new ArgumentException("密码长度必须为 6～18 位。");
    }

    /// <summary>读取当前用户，不存在时拒绝继续处理。</summary>
    private async Task<UserAccount> GetRequiredUserAsync(long userId, CancellationToken cancellationToken) =>
        await GetUserAsync(userId, cancellationToken)
        ?? throw new UnauthorizedAccessException("当前用户不存在。");

    /// <summary>匹配已确认的用户名字符和长度规则。</summary>
    [GeneratedRegex(@"^[\u4e00-\u9fffA-Za-z0-9_]{3,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
