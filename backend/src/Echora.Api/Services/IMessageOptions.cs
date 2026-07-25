namespace Echora.Api.Services;

/// <summary>iMessage 公开入口与网关内部认证配置。</summary>
public sealed class IMessageOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "IMessage";

    /// <summary>是否向用户开放绑定入口。</summary>
    public bool Enabled { get; init; }

    /// <summary>用户发送绑定消息的公开号码。</summary>
    public string PublicPhone { get; init; } = string.Empty;

    /// <summary>API 与 Spectrum 网关之间共享的内部密钥。</summary>
    public string InternalSecret { get; init; } = string.Empty;

    /// <summary>后端主动发送消息时访问的 Spectrum 网关内部地址。</summary>
    public string GatewayBaseUrl { get; init; } = "http://imessage-gateway:3000";

    /// <summary>一次性绑定码有效分钟数。</summary>
    public int BindingCodeLifetimeMinutes { get; init; } = 10;
}
