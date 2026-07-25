using System.Security.Cryptography;
using Echora.Api.BackgroundJobs;
using Echora.Api.Contracts;
using Echora.Api.Entities;
using Hangfire;
using ImageMagick;
using SkiaSharp;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>校验图片、读取 EXIF、保存原图并维护附件归属。</summary>
public sealed class AttachmentService(
    ISqlSugarClient db,
    IObjectStorage storage,
    ImageMetadataService metadataReader,
    ILogger<AttachmentService> logger,
    IBackgroundJobClient? backgroundJobs = null)
{
    /// <summary>MVP 单张图片最大 25 MB。</summary>
    public const long MaxBytes = 25 * 1024 * 1024;

    /// <summary>保存一张尚未绑定消息或一刻的当前用户图片。</summary>
    public async Task<AttachmentResponse> UploadAsync(
        long userId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaxBytes)
            throw new ArgumentException(file.Length <= 0 ? "图片内容为空。" : "图片超过 25 MB 限制。");

        var originalName = Path.GetFileName(file.FileName.Replace('\\', '/')).Trim();
        if (originalName.Length is 0 or > 255) throw new ArgumentException("文件名无效。");

        await using var source = file.OpenReadStream();
        using var memory = new MemoryStream((int)file.Length);
        await source.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        var mimeType = DetectMimeType(bytes);
        var metadata = metadataReader.Read(bytes);
        var objectKey = BuildObjectKey(userId, mimeType);
        var preview = CreateCompatiblePreview(bytes, mimeType);
        var previewObjectKey = preview is null ? null : BuildObjectKey(userId, "image/jpeg");

        await storage.UploadAsync(objectKey, bytes, mimeType, cancellationToken);
        try
        {
            if (preview is not null)
                await storage.UploadAsync(
                    previewObjectKey!,
                    preview.Value.Bytes,
                    "image/jpeg",
                    cancellationToken);
        }
        catch
        {
            await DeleteOrEnqueueAsync(objectKey);
            throw;
        }
        var attachment = new Attachment
        {
            UserId = userId,
            ObjectKey = objectKey,
            PreviewObjectKey = previewObjectKey,
            OriginalFileName = originalName,
            MimeType = mimeType,
            SizeBytes = bytes.LongLength,
            Width = metadata.Width ?? preview?.Width,
            Height = metadata.Height ?? preview?.Height,
            CapturedAt = metadata.CapturedAt,
            Latitude = metadata.Latitude,
            Longitude = metadata.Longitude,
            Orientation = metadata.Orientation,
            MetadataExtracted = true,
            UploadedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            attachment.Id = await db.Insertable(attachment)
                .ExecuteReturnBigIdentityAsync(cancellationToken);
        }
        catch
        {
            await DeleteObjectsAsync(GetObjectKeys(attachment));
            throw;
        }

        logger.LogInformation(
            "Attachment uploaded: UserId {UserId}, AttachmentId {AttachmentId}, MimeType {MimeType}, ByteCount {ByteCount}",
            userId,
            attachment.Id,
            attachment.MimeType,
            attachment.SizeBytes);
        return ToResponse(attachment);
    }

    /// <summary>读取当前用户的附件元数据。</summary>
    public async Task<Attachment?> GetAsync(
        long userId,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        return await db.Queryable<Attachment>()
            .Where(item => item.Id == attachmentId && item.UserId == userId)
            .FirstAsync(cancellationToken);
    }

    /// <summary>读取当前用户的私有图片内容。</summary>
    public async Task<(Attachment Attachment, byte[] Bytes)?> ReadAsync(
        long userId,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await GetAsync(userId, attachmentId, cancellationToken);
        if (attachment is null) return null;
        return (attachment, await storage.ReadAsync(attachment.ObjectKey, cancellationToken));
    }

    /// <summary>读取浏览器和模型可解码的图片；HEIC/HEIF 使用上传时生成的 JPEG。</summary>
    public async Task<(Attachment Attachment, byte[] Bytes, string MimeType)?> ReadDisplayAsync(
        long userId,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await GetAsync(userId, attachmentId, cancellationToken);
        if (attachment is null) return null;
        var objectKey = attachment.PreviewObjectKey ?? attachment.ObjectKey;
        var mimeType = attachment.PreviewObjectKey is null ? attachment.MimeType : "image/jpeg";
        return (attachment, await storage.ReadAsync(objectKey, cancellationToken), mimeType);
    }

    /// <summary>读取已经完成当前用户归属校验的附件字节。</summary>
    public Task<byte[]> ReadBytesAsync(Attachment attachment, CancellationToken cancellationToken) =>
        storage.ReadAsync(attachment.PreviewObjectKey ?? attachment.ObjectKey, cancellationToken);

    /// <summary>返回传给模型时与字节内容一致的 MIME。</summary>
    public static string GetReadableMimeType(Attachment attachment) =>
        attachment.PreviewObjectKey is null ? attachment.MimeType : "image/jpeg";

    /// <summary>按最长边生成 WebP 缩略图；不在应用内缓存，避免长期占用服务器内存。</summary>
    public async Task<(Attachment Attachment, byte[] Bytes)?> ReadThumbnailAsync(
        long userId,
        long attachmentId,
        int size,
        int quality,
        CancellationToken cancellationToken)
    {
        if (size is < 64 or > 1024) throw new ArgumentOutOfRangeException(nameof(size));
        if (quality is < 40 or > 95) throw new ArgumentOutOfRangeException(nameof(quality));

        var original = await ReadDisplayAsync(userId, attachmentId, cancellationToken);
        if (original is null) return null;
        using var source = SKBitmap.Decode(original.Value.Bytes)
            ?? throw new InvalidOperationException("图片内容无法解码。");
        var ratio = Math.Min(1d, size / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * ratio));
        var height = Math.Max(1, (int)Math.Round(source.Height * ratio));
        using var resized = source.Resize(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None))
            ?? throw new InvalidOperationException("图片缩略图生成失败。");
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, quality);
        return (original.Value.Attachment, encoded.ToArray());
    }

    /// <summary>数据库提交删除后清理一组存储对象，失败项进入 Hangfire 补偿。</summary>
    public async Task DeleteObjectsAsync(IEnumerable<string> objectKeys)
    {
        foreach (var objectKey in objectKeys.Distinct(StringComparer.Ordinal))
            await DeleteOrEnqueueAsync(objectKey);
    }

    /// <summary>删除当前用户尚未绑定来源的图片。</summary>
    public async Task<bool> DeleteUnboundAsync(
        long userId,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await GetAsync(userId, attachmentId, cancellationToken);
        if (attachment is null) return true;
        if (attachment.MessageId is not null || attachment.MomentId is not null) return false;

        await db.Deleteable<Attachment>()
            .Where(item => item.Id == attachmentId && item.UserId == userId)
            .ExecuteCommandAsync(cancellationToken);
        await DeleteObjectsAsync(GetObjectKeys(attachment));
        return true;
    }

    /// <summary>生成按用户隔离且不泄露原文件名的 ObjectKey。</summary>
    public static string BuildObjectKey(long userId, string mimeType)
    {
        var extension = mimeType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            "image/heif" => ".heif",
            _ => throw new ArgumentOutOfRangeException(nameof(mimeType)),
        };
        return $"users/{userId}/{DateTime.UtcNow:yyyy/MM}/{RandomNumberGenerator.GetHexString(16).ToLowerInvariant()}{extension}";
    }

    /// <summary>用文件签名识别实际 MIME，不信任浏览器声明。</summary>
    private static string DetectMimeType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[..3].SequenceEqual(new byte[] { 0xff, 0xd8, 0xff }))
            return "image/jpeg";
        if (bytes.Length >= 8
            && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return "image/png";
        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        var heifType = DetectHeifMimeType(bytes);
        if (heifType is not null) return heifType;
        throw new ArgumentException("当前只支持 JPEG、PNG、WebP、HEIC 和 HEIF 图片。");
    }

    /// <summary>识别 ISO BMFF 的主品牌和兼容品牌，不信任文件扩展名。</summary>
    private static string? DetectHeifMimeType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes.Slice(4, 4).SequenceEqual("ftyp"u8)) return null;
        var boxLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes[..4]);
        var end = Math.Min(bytes.Length, checked((int)boxLength));
        var hasHeif = false;
        for (var offset = 8; offset + 4 <= end; offset = offset == 8 ? 16 : offset + 4)
        {
            var brand = System.Text.Encoding.ASCII.GetString(bytes.Slice(offset, 4));
            if (brand is "heic" or "heix" or "hevc" or "hevx" or "heis" or "heim")
                return "image/heic";
            if (brand is "mif1" or "msf1") hasHeif = true;
        }
        return hasHeif ? "image/heif" : null;
    }

    /// <summary>HEIC/HEIF 仅转换一次为 JPEG，原图仍完整保留。</summary>
    private static CompatiblePreview? CreateCompatiblePreview(byte[] bytes, string mimeType)
    {
        if (mimeType is not ("image/heic" or "image/heif")) return null;
        using var image = new MagickImage(bytes);
        image.AutoOrient();
        image.Format = MagickFormat.Jpeg;
        image.Quality = 88;
        image.Strip();
        return new CompatiblePreview(
            image.ToByteArray(),
            checked((int)image.Width),
            checked((int)image.Height));
    }

    /// <summary>列出附件原图和兼容预览的全部存储 Key。</summary>
    public static IEnumerable<string> GetObjectKeys(Attachment attachment)
    {
        yield return attachment.ObjectKey;
        if (!string.IsNullOrWhiteSpace(attachment.PreviewObjectKey))
            yield return attachment.PreviewObjectKey;
    }

    /// <summary>对象删除失败时交给 Hangfire，不把数据库删除伪装成对象已清理。</summary>
    private async Task DeleteOrEnqueueAsync(string objectKey)
    {
        try
        {
            await storage.DeleteAsync(objectKey, CancellationToken.None);
        }
        catch (Exception exception)
        {
            if (backgroundJobs is not null)
            {
                logger.LogError(
                    exception,
                    "Storage cleanup failed; queued compensation: ObjectKeySuffix {ObjectKeySuffix}",
                    Path.GetFileName(objectKey));
                backgroundJobs.Enqueue<CleanupJob>(job => job.DeleteObjectAsync(objectKey));
            }
            else
            {
                logger.LogError(
                    exception,
                    "Storage cleanup failed and Hangfire is disabled: ObjectKeySuffix {ObjectKeySuffix}",
                    Path.GetFileName(objectKey));
            }
        }
    }

    /// <summary>映射为不暴露 ObjectKey 的 API 回执。</summary>
    public static AttachmentResponse ToResponse(Attachment attachment) => new()
    {
        Id = attachment.Id,
        FileName = attachment.OriginalFileName,
        MimeType = GetReadableMimeType(attachment),
        SizeBytes = attachment.SizeBytes,
        ContentUrl = $"/api/assets/{attachment.Id}/content",
    };

    /// <summary>HEIC/HEIF 的一次性兼容转换结果。</summary>
    private readonly record struct CompatiblePreview(byte[] Bytes, int Width, int Height);
}
