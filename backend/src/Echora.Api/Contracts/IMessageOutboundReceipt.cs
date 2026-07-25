namespace Echora.Api.Contracts;

/// <summary>主动 iMessage 已由 Photon 接受后的最小回执。</summary>
public sealed record IMessageOutboundReceipt(
    string SpaceId,
    string? MessageId,
    DateTimeOffset SentAt);
