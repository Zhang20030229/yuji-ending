using SqlSugar;

namespace Echora.Api.Entities;

/// <summary>一条聊天消息或一刻的三个后台分析分支状态。</summary>
[SugarTable("analysis_runs")]
[SugarIndex("ux_analysis_runs_target_message", nameof(UserId), OrderByType.Asc, nameof(TargetMessageId), OrderByType.Asc, true)]
[SugarIndex("ux_analysis_runs_moment", nameof(UserId), OrderByType.Asc, nameof(MomentId), OrderByType.Asc, true)]
[SugarIndex("ix_analysis_runs_user_life_status", nameof(UserId), OrderByType.Asc, nameof(LifeRecordStatus), OrderByType.Asc)]
[SugarIndex("ix_analysis_runs_recognition_status", nameof(RecognitionStatus), OrderByType.Asc)]
[SugarIndex("ix_analysis_runs_emotion_status", nameof(EmotionStatus), OrderByType.Asc)]
public sealed class AnalysisRun
{
    /// <summary>数据库自增主键。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>拥有该运行记录的用户。</summary>
    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    /// <summary>聊天消息来源的会话 ID；一刻来源为空。</summary>
    [SugarColumn(ColumnName = "conversation_id", IsNullable = true)]
    public long? ConversationId { get; set; }

    /// <summary>本次唯一分析的 User Message ID；一刻来源为空。</summary>
    [SugarColumn(ColumnName = "target_message_id", IsNullable = true)]
    public long? TargetMessageId { get; set; }

    /// <summary>本次唯一分析的一刻 ID；聊天来源为空。</summary>
    [SugarColumn(ColumnName = "moment_id", IsNullable = true)]
    public long? MomentId { get; set; }

    /// <summary>LifeRecord 分支状态。</summary>
    [SugarColumn(ColumnName = "life_record_status", Length = 20)]
    public string LifeRecordStatus { get; set; } = "Pending";

    /// <summary>Recognition 分支状态。</summary>
    [SugarColumn(ColumnName = "recognition_status", Length = 20)]
    public string RecognitionStatus { get; set; } = "Pending";

    /// <summary>Emotion 分支状态。</summary>
    [SugarColumn(ColumnName = "emotion_status", Length = 20)]
    public string EmotionStatus { get; set; } = "Pending";

    /// <summary>LifeRecord 分支尝试次数。</summary>
    [SugarColumn(ColumnName = "life_record_attempts")]
    public int LifeRecordAttempts { get; set; }

    /// <summary>Recognition 分支尝试次数。</summary>
    [SugarColumn(ColumnName = "recognition_attempts")]
    public int RecognitionAttempts { get; set; }

    /// <summary>Emotion 分支尝试次数。</summary>
    [SugarColumn(ColumnName = "emotion_attempts")]
    public int EmotionAttempts { get; set; }

    /// <summary>LifeRecord 最近一次安全错误摘要。</summary>
    [SugarColumn(ColumnName = "life_record_error", ColumnDataType = "text", IsNullable = true)]
    public string? LifeRecordError { get; set; }

    /// <summary>Recognition 最近一次安全错误摘要。</summary>
    [SugarColumn(ColumnName = "recognition_error", ColumnDataType = "text", IsNullable = true)]
    public string? RecognitionError { get; set; }

    /// <summary>Emotion 最近一次安全错误摘要。</summary>
    [SugarColumn(ColumnName = "emotion_error", ColumnDataType = "text", IsNullable = true)]
    public string? EmotionError { get; set; }

    /// <summary>LifeRecord 最近一次执行耗时。</summary>
    [SugarColumn(ColumnName = "life_record_duration_ms", IsNullable = true)]
    public long? LifeRecordDurationMs { get; set; }

    /// <summary>Recognition 最近一次执行耗时。</summary>
    [SugarColumn(ColumnName = "recognition_duration_ms", IsNullable = true)]
    public long? RecognitionDurationMs { get; set; }

    /// <summary>Emotion 最近一次执行耗时。</summary>
    [SugarColumn(ColumnName = "emotion_duration_ms", IsNullable = true)]
    public long? EmotionDurationMs { get; set; }

    /// <summary>当前入队的 Hangfire Job ID。</summary>
    [SugarColumn(ColumnName = "hangfire_job_id", Length = 100, IsNullable = true)]
    public string? HangfireJobId { get; set; }

    /// <summary>分析任务建立时间。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最近一次状态变化时间。</summary>
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "timestamptz")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
