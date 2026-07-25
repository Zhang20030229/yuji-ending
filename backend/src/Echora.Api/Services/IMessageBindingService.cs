using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Echora.Api.Contracts;
using Echora.Api.Entities;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>创建一次性绑定码，并把 Photon 发件身份解析为遇己用户。</summary>
public sealed class IMessageBindingService(
    ISqlSugarClient db,
    IMessageOptions options)
{
    /// <summary>读取当前账号的绑定状态。</summary>
    public async Task<IMessageBindingResponse> GetAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        var binding = await db.Queryable<IMessageBinding>()
            .Where(item => item.UserId == userId)
            .FirstAsync(cancellationToken);
        return new IMessageBindingResponse(
            IsConfigured,
            binding?.SenderId is not null,
            Mask(binding?.SenderId),
            IsConfigured ? options.PublicPhone : null,
            binding?.BoundAt);
    }

    /// <summary>读取主动发送所需的已绑定 Photon 发件身份。</summary>
    public async Task<string> GetBoundSenderIdAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var senderId = await db.Queryable<IMessageBinding>()
            .Where(item => item.UserId == userId)
            .Select(item => item.SenderId)
            .FirstAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(senderId)
            ? throw new InvalidOperationException("该账号尚未连接 iMessage。")
            : senderId;
    }

    /// <summary>按 Photon 发件身份读取已绑定用户；图片消息不能执行绑定命令。</summary>
    public async Task<long?> ResolveBoundUserAsync(
        string senderId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var binding = await db.Queryable<IMessageBinding>()
            .Where(item => item.SenderId == senderId)
            .FirstAsync(cancellationToken);
        return binding?.UserId;
    }

    /// <summary>生成短期六位数字绑定码；同一用户的新码会立即替换旧码。</summary>
    public async Task<IMessageBindingCodeResponse> CreateCodeAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(options.BindingCodeLifetimeMinutes);
        var binding = await db.Queryable<IMessageBinding>()
            .Where(item => item.UserId == userId)
            .FirstAsync(cancellationToken);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000)
                .ToString("D6", CultureInfo.InvariantCulture);
            var hash = HashCode(code);
            if (await db.Queryable<IMessageBinding>()
                    .AnyAsync(item => item.BindingCodeHash == hash && item.UserId != userId, cancellationToken))
                continue;

            if (binding is null)
            {
                binding = new IMessageBinding
                {
                    UserId = userId,
                    BindingCodeHash = hash,
                    BindingCodeExpiresAt = expiresAt,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                binding.Id = await db.Insertable(binding)
                    .ExecuteReturnBigIdentityAsync(cancellationToken);
            }
            else
            {
                binding.BindingCodeHash = hash;
                binding.BindingCodeExpiresAt = expiresAt;
                binding.UpdatedAt = now;
                await db.Updateable(binding).ExecuteCommandAsync(cancellationToken);
            }

            return new IMessageBindingCodeResponse(code, options.PublicPhone, expiresAt);
        }

        throw new InvalidOperationException("暂时无法生成绑定码，请稍后重试。");
    }

    /// <summary>解除当前账号与 iMessage 发件身份的绑定。</summary>
    public async Task<bool> DisconnectAsync(long userId, CancellationToken cancellationToken)
    {
        var binding = await db.Queryable<IMessageBinding>()
            .Where(item => item.UserId == userId)
            .FirstAsync(cancellationToken);
        if (binding is null) return false;

        binding.SenderId = null;
        binding.BoundAt = null;
        binding.BindingCodeHash = null;
        binding.BindingCodeExpiresAt = null;
        binding.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(binding).ExecuteCommandAsync(cancellationToken);
        return true;
    }

    /// <summary>处理“绑定 123456”或解析已绑定发件人。</summary>
    public async Task<IMessageSenderResolution> ResolveAsync(
        string senderId,
        string text,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var code = TryReadBindingCode(text);
        if (code is not null)
            return await BindAsync(senderId, code, cancellationToken);

        var binding = await db.Queryable<IMessageBinding>()
            .Where(item => item.SenderId == senderId)
            .FirstAsync(cancellationToken);
        return binding is null
            ? new IMessageSenderResolution()
            : new IMessageSenderResolution(binding.UserId);
    }

    /// <summary>以固定时间比较验证 API 与网关的共享密钥。</summary>
    public bool IsGatewayAuthorized(string? supplied)
    {
        if (!IsConfigured || string.IsNullOrEmpty(supplied)) return false;
        var expectedBytes = Encoding.UTF8.GetBytes(options.InternalSecret);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    /// <summary>把合法绑定码关联到发件人；一个发件人只能绑定一个账号。</summary>
    private async Task<IMessageSenderResolution> BindAsync(
        string senderId,
        string code,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var binding = await db.Queryable<IMessageBinding>()
            .Where(item => item.BindingCodeHash == HashCode(code)
                && item.BindingCodeExpiresAt > now)
            .FirstAsync(cancellationToken);
        if (binding is null)
            return new IMessageSenderResolution(Reply: "绑定码无效或已经过期，请在遇己设置中重新生成。");

        var occupied = await db.Queryable<IMessageBinding>()
            .AnyAsync(item => item.SenderId == senderId && item.UserId != binding.UserId, cancellationToken);
        if (occupied)
            return new IMessageSenderResolution(Reply: "这个 iMessage 已绑定其他遇己账号，请先在原账号中解除绑定。");

        binding.SenderId = senderId;
        binding.BoundAt = now;
        binding.BindingCodeHash = null;
        binding.BindingCodeExpiresAt = null;
        binding.UpdatedAt = now;
        await db.Updateable(binding).ExecuteCommandAsync(cancellationToken);
        return new IMessageSenderResolution(
            binding.UserId,
            "连接成功。以后直接在这里发文字，就能和遇己对话。");
    }

    /// <summary>读取严格的六位数字绑定命令。</summary>
    private static string? TryReadBindingCode(string text)
    {
        var parts = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && parts[0] == "绑定"
            && parts[1].Length == 6
            && parts[1].All(char.IsAsciiDigit)
            ? parts[1]
            : null;
    }

    /// <summary>使用内部密钥生成不可逆绑定码指纹。</summary>
    private string HashCode(string code)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.InternalSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(code)))
            .ToLowerInvariant();
    }

    /// <summary>只向设置页展示发件标识首尾，避免暴露完整地址。</summary>
    private static string? Mask(string? senderId)
    {
        if (string.IsNullOrWhiteSpace(senderId)) return null;
        return senderId.Length <= 6
            ? new string('•', senderId.Length)
            : $"{senderId[..3]}••••{senderId[^3..]}";
    }

    /// <summary>确保公开号码、内部密钥和绑定码时限已经配置。</summary>
    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("iMessage 连接暂未开放。");
    }

    /// <summary>只有完整配置才对外开放入口。</summary>
    private bool IsConfigured =>
        options.Enabled
        && !string.IsNullOrWhiteSpace(options.PublicPhone)
        && Encoding.UTF8.GetByteCount(options.InternalSecret) >= 32
        && options.BindingCodeLifetimeMinutes is >= 1 and <= 60;
}
