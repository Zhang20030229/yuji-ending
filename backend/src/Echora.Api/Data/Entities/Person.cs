using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>跨会话汇总的一张人物主卡。</summary>
[SugarTable("people")]
[SugarIndex("ix_people_user_normalized_name", nameof(UserId), OrderByType.Asc, nameof(NormalizedName), OrderByType.Asc)]
[SugarIndex("ix_people_user_relationship", nameof(UserId), OrderByType.Asc, nameof(Relationship), OrderByType.Asc)]
public sealed class Person
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该人物卡的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>人物卡主要名称。</summary>
    [SugarColumn(ColumnName = "name", Length = 100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>服务端生成的标准化搜索名称。</summary>
    [SugarColumn(ColumnName = "normalized_name", Length = 100)]
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>人物与用户的固定关系大类。</summary>
    [SugarColumn(ColumnName = "relationship", Length = 20)]
    public string Relationship { get; set; } = "Unknown";

    /// <summary>大学同学、基友等自由关系关键词。</summary>
    [SugarColumn(ColumnName = "relationship_keywords", ColumnDataType = "text[]", IsArray = true)]
    public string[] RelationshipKeywords { get; set; } = [];

    /// <summary>最近一张已确认关联的封面图片。</summary>
    [SugarColumn(ColumnName = "cover_attachment_id", IsNullable = true)]
    public long? CoverAttachmentId { get; set; }

    /// <summary>人物卡创建时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>人物卡最后更新时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
