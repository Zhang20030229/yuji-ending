using Echora.Api.Entities;
using Echora.Api.Services;
using Hangfire;
using SqlSugar;

namespace Echora.Api.Jobs;

/// <summary>把结构化记忆记录增量同步为向量。</summary>
public sealed class MemoryEmbeddingJob(
    ISqlSugarClient db,
    MemoryEmbeddingService embeddings,
    ILogger<MemoryEmbeddingJob> logger)
{
    /// <summary>单个用户的增量同步；瞬时失败重试两次后由巡检兜底。</summary>
    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(long userId, CancellationToken cancellationToken)
    {
        var written = await embeddings.SynchronizeAsync(userId, cancellationToken);
        logger.LogInformation(
            "Memory embedding job completed: UserId {UserId}, Written {Written}.",
            userId,
            written);
    }

    /// <summary>巡检补齐存量与此前失败的用户；单个用户失败不影响其他用户。</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task BackfillAsync(CancellationToken cancellationToken)
    {
        var userIds = await db.Queryable<UserAccount>()
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var written = 0;
        var failed = 0;
        foreach (var userId in userIds)
        {
            try
            {
                written += await embeddings.SynchronizeAsync(userId, cancellationToken);
            }
            catch (Exception exception)
            {
                failed++;
                logger.LogWarning(exception, "Memory embedding backfill failed: UserId {UserId}.", userId);
            }
        }
        // 孤儿清理是七张表的反连接，成本高且不紧急，交给每日维护任务执行。
        logger.LogInformation(
            "Memory embedding backfill completed: Users {Users}, Written {Written}, Failed {Failed}.",
            userIds.Count,
            written,
            failed);
    }
}
