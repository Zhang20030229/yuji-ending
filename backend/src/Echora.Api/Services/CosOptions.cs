namespace Echora.Api.Services;

/// <summary>腾讯云 COS 的固定服务端配置。</summary>
public sealed class CosOptions
{
    /// <summary>配置文件中的节名称。</summary>
    public const string SectionName = "COS";

    /// <summary>COS 地域，例如 ap-guangzhou。</summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>包含 APPID 的完整存储桶名称。</summary>
    public string Bucket { get; init; } = string.Empty;

    /// <summary>服务端使用的腾讯云 SecretId。</summary>
    public string SecretId { get; init; } = string.Empty;

    /// <summary>服务端使用的腾讯云 SecretKey。</summary>
    public string SecretKey { get; init; } = string.Empty;
}
