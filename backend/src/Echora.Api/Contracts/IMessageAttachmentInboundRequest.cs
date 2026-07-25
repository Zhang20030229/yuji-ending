namespace Echora.Api.Contracts;

/// <summary>Spectrum 网关传入的一条 iMessage 图片消息。</summary>
public sealed class IMessageAttachmentInboundRequest
{
    /// <summary>Photon 消息标识。</summary>
    public string MessageId { get; init; } = string.Empty;

    /// <summary>Photon 私聊空间标识。</summary>
    public string SpaceId { get; init; } = string.Empty;

    /// <summary>Photon 发件身份。</summary>
    public string SenderId { get; init; } = string.Empty;

    /// <summary>消息发送时间。</summary>
    public DateTimeOffset SentAt { get; init; }

    /// <summary>图片消息附带的可选文字。</summary>
    public string? Text { get; init; }

    /// <summary>Photon 提供的原始图片文件。</summary>
    public List<IFormFile> Files { get; init; } = [];
}
