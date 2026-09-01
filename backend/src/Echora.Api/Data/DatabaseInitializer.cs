using Echora.Api.Entities;
using SqlSugar;

namespace Echora.Api.Data;

/// <summary>使用 SqlSugar Code First 建立当前多用户业务表。</summary>
public static class DatabaseInitializer
{
    /// <summary>显式同步当前实体；Hangfire 表由 Hangfire 自己维护。</summary>
    public static void Initialize(ISqlSugarClient db, int embeddingDimensions, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        db.CodeFirst.InitTables(
            typeof(UserAccount),
            typeof(IMessageBinding),
            typeof(Conversation),
            typeof(ConversationMessage),
            typeof(Moment),
            typeof(Attachment),
            typeof(Person),
            typeof(PersonAlias),
            typeof(PersonRecord),
            typeof(Place),
            typeof(PlaceAlias),
            typeof(PlaceRecord),
            typeof(LifeEvent),
            typeof(Recognition),
            typeof(EmotionRecord),
            typeof(CbtObservation),
            typeof(EmotionSummary),
            typeof(DayDigest),
            typeof(UnresolvedMention),
            typeof(AnalysisRun),
            typeof(ReportPack),
            typeof(ReportSection),
            typeof(WellbeingAssessment));
        // 实体之外的 PostgreSQL 专属检索结构在同一入口补齐。
        VectorSchema.Initialize(db, embeddingDimensions, logger);
    }
}
