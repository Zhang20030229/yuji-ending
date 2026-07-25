using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>一个人物在用户表达中使用的其他名称。</summary>
[SugarTable("person_aliases")]
[SugarIndex("ux_person_aliases_user_person_name", nameof(UserId), OrderByType.Asc, nameof(PersonId), OrderByType.Asc, nameof(NormalizedName), OrderByType.Asc, true)]
[SugarIndex("ix_person_aliases_user_name", nameof(UserId), OrderByType.Asc, nameof(NormalizedName), OrderByType.Asc)]
public sealed class PersonAlias
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该别名的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>所属人物 ID。</summary>
    [SugarColumn(ColumnName = "person_id")]
    public long PersonId { get; set; }

    /// <summary>用户使用的原始别名。</summary>
    [SugarColumn(ColumnName = "name", Length = 100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>服务端生成的标准化搜索值。</summary>
    [SugarColumn(ColumnName = "normalized_name", Length = 100)]
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>首次明确别名关系的 User Message。</summary>
    [SugarColumn(ColumnName = "source_message_id", IsNullable = true)]
    public long? SourceMessageId { get; set; }

    /// <summary>首次明确别名关系的一刻；聊天来源为空。</summary>
    [SugarColumn(ColumnName = "source_moment_id", IsNullable = true)]
    public long? SourceMomentId { get; set; }

    /// <summary>别名创建时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
