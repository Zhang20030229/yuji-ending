using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>跨会话汇总的一张地点主卡。</summary>
[SugarTable("places")]
[SugarIndex("ix_places_user_normalized_name", nameof(UserId), OrderByType.Asc, nameof(NormalizedName), OrderByType.Asc)]
[SugarIndex("ix_places_user_region", nameof(UserId), OrderByType.Asc, nameof(Province), OrderByType.Asc, nameof(City), OrderByType.Asc)]
public sealed class Place
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该地点卡的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>地点卡主要名称。</summary>
    [SugarColumn(ColumnName = "name", Length = 150)]
    public string Name { get; set; } = string.Empty;

    /// <summary>服务端生成的标准化搜索名称。</summary>
    [SugarColumn(ColumnName = "normalized_name", Length = 150)]
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>会话明确提供的省。</summary>
    [SugarColumn(ColumnName = "province", Length = 50, IsNullable = true)]
    public string? Province { get; set; }

    /// <summary>会话明确提供的市。</summary>
    [SugarColumn(ColumnName = "city", Length = 50, IsNullable = true)]
    public string? City { get; set; }

    /// <summary>从已关联照片 EXIF 读取的代表纬度；没有可靠坐标时为空。</summary>
    [SugarColumn(ColumnName = "latitude", IsNullable = true)]
    public double? Latitude { get; set; }

    /// <summary>从已关联照片 EXIF 读取的代表经度；没有可靠坐标时为空。</summary>
    [SugarColumn(ColumnName = "longitude", IsNullable = true)]
    public double? Longitude { get; set; }

    /// <summary>最近一张已确认关联的封面图片。</summary>
    [SugarColumn(ColumnName = "cover_attachment_id", IsNullable = true)]
    public long? CoverAttachmentId { get; set; }

    /// <summary>地点卡创建时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>地点卡最后更新时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
