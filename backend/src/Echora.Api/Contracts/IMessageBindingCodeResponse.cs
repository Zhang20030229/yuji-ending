namespace Echora.Api.Contracts;

/// <summary>一次性返回给当前登录用户的 iMessage 绑定码。</summary>
public sealed record IMessageBindingCodeResponse(
    string Code,
    string PublicPhone,
    DateTimeOffset ExpiresAt);
