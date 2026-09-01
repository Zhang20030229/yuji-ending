using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>洞察日视图使用的当日 AI 摘要：一句当日总结和最多三条关键洞察。</summary>
[SugarTable("day_digests")]
[SugarIndex("ux_day_digests_user_date", nameof(UserId), OrderByType.Asc, nameof(Date), OrderByType.Asc, true)]
public sealed class DayDigest
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该摘要的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>上海时区自然日。</summary>
    [SugarColumn(ColumnName = "date", ColumnDataType = "date")]
    public DateOnly Date { get; set; }

    /// <summary>当日总结：一到两句口语化回看，没有可写内容时为空字符串。</summary>
    [SugarColumn(ColumnName = "narrative", ColumnDataType = "text")]
    public string Narrative { get; set; } = string.Empty;

    /// <summary>关键洞察数组的 JSON，最多三条。</summary>
    [SugarColumn(ColumnName = "insights_json", ColumnDataType = "text")]
    public string InsightsJson { get; set; } = "[]";

    /// <summary>当天情绪记录与 CBT 观察的输入指纹；相同则不再调用模型。</summary>
    [SugarColumn(ColumnName = "source_fingerprint", ColumnDataType = "text")]
    public string SourceFingerprint { get; set; } = string.Empty;

    /// <summary>摘要最后重算时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
