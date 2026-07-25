namespace Echora.Api.Contracts;

/// <summary>设置页所需的最小 iMessage 绑定状态。</summary>
public sealed record IMessageBindingResponse(
    bool Enabled,
    bool IsBound,
    string? MaskedSender,
    string? PublicPhone,
    DateTimeOffset? BoundAt);
