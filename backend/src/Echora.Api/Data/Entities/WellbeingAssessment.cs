using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>用户主动完成的一次 WHO-5 心理幸福感自评。</summary>
[SugarTable("wellbeing_assessments")]
[SugarIndex("ix_wellbeing_assessments_user_time", nameof(UserId), OrderByType.Asc, nameof(AssessedAt), OrderByType.Desc)]
public sealed class WellbeingAssessment
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>完成本次自评的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>量表名称，第一版固定为 WHO-5。</summary>
    [SugarColumn(ColumnName = "instrument", Length = 20)]
    public string Instrument { get; set; } = "WHO-5";

    /// <summary>五道题分别选择的 0～5 分。</summary>
    [SugarColumn(ColumnName = "answers", ColumnDataType = "int4[]", IsArray = true)]
    public int[] Answers { get; set; } = [];

    /// <summary>五题相加得到的原始分，范围 0～25。</summary>
    [SugarColumn(ColumnName = "raw_score")]
    public int RawScore { get; set; }

    /// <summary>原始分乘以 4 得到的百分制分数。</summary>
    [SugarColumn(ColumnName = "percentage_score")]
    public int PercentageScore { get; set; }

    /// <summary>用户提交自评的时间。</summary>
    [SugarColumn(ColumnName = "assessed_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset AssessedAt { get; set; } = DateTimeOffset.UtcNow;
}
