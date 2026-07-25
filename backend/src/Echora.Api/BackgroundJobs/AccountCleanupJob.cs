using Echora.Api.Entities;
using Echora.Api.Services;
using Hangfire;
using SqlSugar;

namespace Echora.Api.BackgroundJobs;

/// <summary>删除已经停用账号的全部业务数据和私有附件。</summary>
public sealed class AccountCleanupJob(
    ISqlSugarClient db,
    IObjectStorage storage,
    ILogger<AccountCleanupJob> logger)
{
    /// <summary>先幂等删除文件，再在一个事务中删除用户全部业务记录。</summary>
    [AutomaticRetry(Attempts = 5, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(long userId)
    {
        var storedAttachments = await db.Queryable<Attachment>()
            .Where(item => item.UserId == userId)
            .ToListAsync();
        var objectKeys = storedAttachments.SelectMany(AttachmentService.GetObjectKeys).ToArray();
        foreach (var objectKey in objectKeys.Distinct(StringComparer.Ordinal))
            await storage.DeleteAsync(objectKey, CancellationToken.None);

        db.Ado.BeginTran();
        try
        {
            await db.Deleteable<WellbeingAssessment>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<ReportSection>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<ReportPack>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<UnresolvedMention>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<EmotionSummary>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<CbtObservation>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<EmotionRecord>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<Recognition>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<LifeEvent>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<PersonRecord>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<PersonAlias>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<Person>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<PlaceRecord>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<PlaceAlias>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<Place>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<AnalysisRun>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<Attachment>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<Moment>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<ConversationMessage>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<Conversation>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<IMessageBinding>().Where(item => item.UserId == userId).ExecuteCommandAsync();
            await db.Deleteable<UserAccount>().Where(item => item.Id == userId).ExecuteCommandAsync();
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }

        logger.LogInformation(
            "Account cleanup completed: UserId {UserId}, ObjectCount {ObjectCount}",
            userId,
            objectKeys.Length);
    }
}
