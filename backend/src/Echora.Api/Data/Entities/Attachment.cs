using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>一张原始图片的私有存储引用及其可查询元数据。</summary>
[SugarTable("attachments")]
[SugarIndex("ux_attachments_object_key", nameof(ObjectKey), OrderByType.Asc, true)]
[SugarIndex("ix_attachments_user_uploaded", nameof(UserId), OrderByType.Asc, nameof(UploadedAt), OrderByType.Desc)]
public sealed class Attachment
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该图片的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>聊天图片来源；一刻图片为空。</summary>
    [SugarColumn(ColumnName = "message_id", IsNullable = true)]
    public long? MessageId { get; set; }

    /// <summary>一刻图片来源；聊天图片为空。</summary>
    [SugarColumn(ColumnName = "moment_id", IsNullable = true)]
    public long? MomentId { get; set; }

    /// <summary>本地目录或 COS 中不公开的对象 Key。</summary>
    [SugarColumn(ColumnName = "object_key", Length = 500)]
    public string ObjectKey { get; set; } = string.Empty;

    /// <summary>HEIC/HEIF 转换出的浏览器和模型兼容 JPEG；普通图片为空。</summary>
    [SugarColumn(ColumnName = "preview_object_key", Length = 500, IsNullable = true)]
    public string? PreviewObjectKey { get; set; }

    /// <summary>用户上传时的原始文件名。</summary>
    [SugarColumn(ColumnName = "original_file_name", Length = 255)]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>由文件内容确认的 MIME 类型。</summary>
    [SugarColumn(ColumnName = "mime_type", Length = 100)]
    public string MimeType { get; set; } = string.Empty;

    /// <summary>原文件大小，单位字节。</summary>
    [SugarColumn(ColumnName = "size_bytes")]
    public long SizeBytes { get; set; }

    /// <summary>图片宽度。</summary>
    [SugarColumn(ColumnName = "width", IsNullable = true)]
    public int? Width { get; set; }

    /// <summary>图片高度。</summary>
    [SugarColumn(ColumnName = "height", IsNullable = true)]
    public int? Height { get; set; }

    /// <summary>EXIF 原始拍摄时间。</summary>
    [SugarColumn(ColumnName = "captured_at", ColumnDataType = "timestamptz", IsNullable = true)]
    public DateTimeOffset? CapturedAt { get; set; }

    /// <summary>EXIF 纬度。</summary>
    [SugarColumn(ColumnName = "latitude", IsNullable = true)]
    public double? Latitude { get; set; }

    /// <summary>EXIF 经度。</summary>
    [SugarColumn(ColumnName = "longitude", IsNullable = true)]
    public double? Longitude { get; set; }

    /// <summary>EXIF 图片方向。</summary>
    [SugarColumn(ColumnName = "orientation", IsNullable = true)]
    public int? Orientation { get; set; }

    /// <summary>服务端是否成功读取图片元数据。</summary>
    [SugarColumn(ColumnName = "metadata_extracted")]
    public bool MetadataExtracted { get; set; }

    /// <summary>LifeRecordSubagent 生成的客观图片描述。</summary>
    [SugarColumn(ColumnName = "ai_description", ColumnDataType = "text", IsNullable = true)]
    public string? AiDescription { get; set; }

    /// <summary>上传完成时间。</summary>
    [SugarColumn(ColumnName = "uploaded_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}
