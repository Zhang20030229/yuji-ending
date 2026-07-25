namespace Echora.Api.Contracts;

/// <summary>前端上传图片后的回执。</summary>
public sealed class AttachmentResponse
{
    /// <summary>附件数据库 ID。</summary>
    public long Id { get; init; }

    /// <summary>原始文件名。</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>已验证的图片 MIME。</summary>
    public string MimeType { get; init; } = string.Empty;

    /// <summary>原始文件大小。</summary>
    public long SizeBytes { get; init; }

    /// <summary>当前用户访问图片内容的 API。</summary>
    public string ContentUrl { get; init; } = string.Empty;
}
