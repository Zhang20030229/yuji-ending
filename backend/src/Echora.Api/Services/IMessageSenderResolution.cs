namespace Echora.Api.Services;

/// <summary>一次 iMessage 发件身份解析结果。</summary>
public sealed record IMessageSenderResolution(
    long? UserId = null,
    string? Reply = null);
