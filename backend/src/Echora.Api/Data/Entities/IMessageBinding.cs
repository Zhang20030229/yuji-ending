using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>一个遇己账号与一个 iMessage 发件身份的绑定。</summary>
[SugarTable("imessage_bindings")]
[SugarIndex("ux_imessage_bindings_user", nameof(UserId), OrderByType.Asc, true)]
[SugarIndex("ux_imessage_bindings_sender", nameof(SenderId), OrderByType.Asc, true)]
[SugarIndex("ux_imessage_bindings_code", nameof(BindingCodeHash), OrderByType.Asc, true)]
public sealed class IMessageBinding
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>绑定所属的遇己用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>Photon 提供的稳定发件人标识；未完成绑定时为空。</summary>
    [SugarColumn(ColumnName = "sender_id", Length = 500, IsNullable = true)]
    public string? SenderId { get; set; }

    /// <summary>短期绑定码的 HMAC，不保存绑定码明文。</summary>
    [SugarColumn(ColumnName = "binding_code_hash", Length = 64, IsNullable = true)]
    public string? BindingCodeHash { get; set; }

    /// <summary>绑定码失效时间。</summary>
    [SugarColumn(ColumnName = "binding_code_expires_at", ColumnDataType = "timestamptz", IsNullable = true)]
    public DateTimeOffset? BindingCodeExpiresAt { get; set; }

    /// <summary>最近一次完成绑定的时间。</summary>
    [SugarColumn(ColumnName = "bound_at", ColumnDataType = "timestamptz", IsNullable = true)]
    public DateTimeOffset? BoundAt { get; set; }

    /// <summary>记录创建时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>记录最后更新时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
