using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>一个注册用户的登录凭据和最小个人资料。</summary>
[SugarTable("user_accounts")]
[SugarIndex("ux_user_accounts_normalized_username", nameof(NormalizedUsername), OrderByType.Asc, true)]
public sealed class UserAccount
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>用户注册时填写并用于展示的用户名。</summary>
    [SugarColumn(ColumnName = "username", Length = 20)]
    public string Username { get; set; } = string.Empty;

    /// <summary>忽略英文字母大小写后的用户名，用于登录和唯一约束。</summary>
    [SugarColumn(ColumnName = "normalized_username", Length = 20)]
    public string NormalizedUsername { get; set; } = string.Empty;

    /// <summary>ASP.NET Core PasswordHasher 生成的密码摘要。</summary>
    [SugarColumn(ColumnName = "password_hash", Length = 255)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>修改密码后递增，使旧 JWT 失效。</summary>
    [SugarColumn(ColumnName = "token_version")]
    public int TokenVersion { get; set; }

    /// <summary>AI 在对话中使用的用户称呼。</summary>
    [SugarColumn(ColumnName = "display_name", Length = 50)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>用户性别，只允许 Male 或 Female。</summary>
    [SugarColumn(ColumnName = "gender", Length = 10)]
    public string Gender { get; set; } = string.Empty;

    /// <summary>用户出生年份；未完成资料时为空。</summary>
    [SugarColumn(ColumnName = "birth_year", IsNullable = true)]
    public int? BirthYear { get; set; }

    /// <summary>用户出生月份，范围 1～12；未完成资料时为空。</summary>
    [SugarColumn(ColumnName = "birth_month", IsNullable = true)]
    public int? BirthMonth { get; set; }

    /// <summary>用户给 AI 伙伴取的名字。</summary>
    [SugarColumn(ColumnName = "ai_name", Length = 50)]
    public string AiName { get; set; } = string.Empty;

    /// <summary>是否已经完成首次个人资料。</summary>
    [SugarColumn(ColumnName = "profile_completed")]
    public bool ProfileCompleted { get; set; }

    /// <summary>账号创建时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>账号或资料最后修改时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
