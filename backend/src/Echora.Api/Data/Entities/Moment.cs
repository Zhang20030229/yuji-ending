using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>用户通过快捷入口发布的一张照片记录。</summary>
[SugarTable("moments")]
[SugarIndex("ux_moments_user_attachment", nameof(UserId), OrderByType.Asc, nameof(AttachmentId), OrderByType.Asc, true)]
[SugarIndex("ix_moments_user_published_at", nameof(UserId), OrderByType.Asc, nameof(PublishedAt), OrderByType.Desc)]
[SugarIndex("ix_moments_user_status", nameof(UserId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
public sealed class Moment
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该一刻的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>一刻唯一关联的图片附件 ID。</summary>
    [SugarColumn(ColumnName = "attachment_id")]
    public long AttachmentId { get; set; }

    /// <summary>用户发布时填写的原话。</summary>
    [SugarColumn(ColumnName = "text", ColumnDataType = "text", IsNullable = true)]
    public string? Text { get; set; }

    /// <summary>MomentAgent 生成的卡片标题。</summary>
    [SugarColumn(ColumnName = "title", Length = 150, IsNullable = true)]
    public string? Title { get; set; }

    /// <summary>MomentAgent 生成的温和总结。</summary>
    [SugarColumn(ColumnName = "summary", ColumnDataType = "text", IsNullable = true)]
    public string? Summary { get; set; }

    /// <summary>MomentAgent 生成的最多五个关键词。</summary>
    [SugarColumn(ColumnName = "keywords", ColumnDataType = "text[]", IsArray = true)]
    public string[] Keywords { get; set; } = [];

    /// <summary>照片拍摄时间。</summary>
    [SugarColumn(ColumnName = "captured_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>用户完成发布的时间。</summary>
    [SugarColumn(ColumnName = "published_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset PublishedAt { get; set; }

    /// <summary>发布时设备定位的纬度。</summary>
    [SugarColumn(ColumnName = "latitude", IsNullable = true)]
    public double? Latitude { get; set; }

    /// <summary>发布时设备定位的经度。</summary>
    [SugarColumn(ColumnName = "longitude", IsNullable = true)]
    public double? Longitude { get; set; }

    /// <summary>设备定位误差半径，单位米。</summary>
    [SugarColumn(ColumnName = "accuracy_meters", IsNullable = true)]
    public double? AccuracyMeters { get; set; }

    /// <summary>设备位置的采集时间。</summary>
    [SugarColumn(ColumnName = "location_captured_at", ColumnDataType = "timestamptz", IsNullable = true)]
    public DateTimeOffset? LocationCapturedAt { get; set; }

    /// <summary>定位服务解析出的附近地点或区域名称。</summary>
    [SugarColumn(ColumnName = "location_name", Length = 200, IsNullable = true)]
    public string? LocationName { get; set; }

    /// <summary>定位服务解析出的完整地址。</summary>
    [SugarColumn(ColumnName = "location_address", Length = 500, IsNullable = true)]
    public string? LocationAddress { get; set; }

    /// <summary>定位服务解析出的省。</summary>
    [SugarColumn(ColumnName = "province", Length = 50, IsNullable = true)]
    public string? Province { get; set; }

    /// <summary>定位服务解析出的市。</summary>
    [SugarColumn(ColumnName = "city", Length = 50, IsNullable = true)]
    public string? City { get; set; }

    /// <summary>MomentAgent 状态：Pending、Running、Succeeded 或 Failed。</summary>
    [SugarColumn(ColumnName = "status", Length = 20)]
    public string Status { get; set; } = "Pending";

    /// <summary>MomentAgent 已经取得执行权的次数。</summary>
    [SugarColumn(ColumnName = "attempt_count")]
    public int AttemptCount { get; set; }

    /// <summary>MomentAgent 最近一次安全错误摘要。</summary>
    [SugarColumn(ColumnName = "error_message", ColumnDataType = "text", IsNullable = true)]
    public string? ErrorMessage { get; set; }

    /// <summary>MomentAgent 最近一次执行耗时。</summary>
    [SugarColumn(ColumnName = "duration_ms", IsNullable = true)]
    public long? DurationMs { get; set; }

    /// <summary>当前 MomentAgent 的 Hangfire Job ID。</summary>
    [SugarColumn(ColumnName = "hangfire_job_id", Length = 100, IsNullable = true)]
    public string? HangfireJobId { get; set; }

    /// <summary>一刻最后更新时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
