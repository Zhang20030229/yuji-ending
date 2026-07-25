namespace Echora.Api.Contracts;

/// <summary>后端要求 Spectrum 网关执行的动作。</summary>
public sealed record IMessageInboundResponse(
    string Action,
    string? Text = null);
