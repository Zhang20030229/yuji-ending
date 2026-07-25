namespace Echora.Api.Contracts;

/// <summary>Spectrum 网关传入的一条 iMessage 文字消息。</summary>
public sealed record IMessageInboundRequest(
    string MessageId,
    string SpaceId,
    string SenderId,
    DateTimeOffset SentAt,
    string Text);
