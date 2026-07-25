using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.WebP;

namespace Echora.Api.Services;

/// <summary>从原始图片读取尺寸、拍摄时间、GPS 和方向。</summary>
public sealed class ImageMetadataService
{
    /// <summary>读取支持的元数据；缺失字段保持为空，不猜测位置。</summary>
    public ImageMetadata Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var directories = ImageMetadataReader.ReadMetadata(stream).ToArray();

        var (width, height) = ReadSize(directories);
        var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        DateTimeOffset? capturedAt = null;
        if (exif?.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var captured) == true)
        {
            // 大多数相机不写时区；MVP 保留照片墙钟时间并使用服务器本地偏移。
            // ponytail: 若以后需要跨时区精确排序，再读取 OffsetTimeOriginal 并保存原始时区字段。
            var unspecified = DateTime.SpecifyKind(captured, DateTimeKind.Unspecified);
            capturedAt = new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
        }

        var gps = directories.OfType<GpsDirectory>().FirstOrDefault()?.GetGeoLocation();
        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        int? orientation = ifd0?.TryGetInt32(ExifDirectoryBase.TagOrientation, out var value) == true
            ? value
            : null;

        return new ImageMetadata(
            width,
            height,
            capturedAt,
            gps?.Latitude,
            gps?.Longitude,
            orientation);
    }

    /// <summary>从 JPEG、PNG 或 WebP 元数据中读取像素尺寸。</summary>
    private static (int? Width, int? Height) ReadSize(IReadOnlyCollection<MetadataExtractor.Directory> directories)
    {
        var jpeg = directories.OfType<JpegDirectory>().FirstOrDefault();
        if (jpeg is not null)
            return (jpeg.GetInt32(JpegDirectory.TagImageWidth), jpeg.GetInt32(JpegDirectory.TagImageHeight));

        var png = directories.OfType<PngDirectory>().FirstOrDefault();
        if (png is not null
            && png.TryGetInt32(PngDirectory.TagImageWidth, out var pngWidth)
            && png.TryGetInt32(PngDirectory.TagImageHeight, out var pngHeight))
            return (pngWidth, pngHeight);

        var webp = directories.OfType<WebPDirectory>().FirstOrDefault();
        if (webp is not null
            && webp.TryGetInt32(WebPDirectory.TagImageWidth, out var webpWidth)
            && webp.TryGetInt32(WebPDirectory.TagImageHeight, out var webpHeight))
            return (webpWidth, webpHeight);

        return (null, null);
    }
}

/// <summary>图片中可查询的最小元数据。</summary>
public readonly record struct ImageMetadata(
    int? Width,
    int? Height,
    DateTimeOffset? CapturedAt,
    double? Latitude,
    double? Longitude,
    int? Orientation);
