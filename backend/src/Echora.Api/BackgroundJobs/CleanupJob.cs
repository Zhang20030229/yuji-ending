using Hangfire;
using Echora.Api.Services;

namespace Echora.Api.BackgroundJobs;

/// <summary>补偿删除数据库已经不再引用的附件对象。</summary>
public sealed class CleanupJob(IObjectStorage storage, ILogger<CleanupJob> logger)
{
    /// <summary>删除指定对象；抛出真实异常让 Hangfire 执行有限重试。</summary>
    [AutomaticRetry(Attempts = 5, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task DeleteObjectAsync(string objectKey)
    {
        logger.LogInformation(
            "Storage cleanup started: ObjectKeySuffix {ObjectKeySuffix}",
            Path.GetFileName(objectKey));
        await storage.DeleteAsync(objectKey, CancellationToken.None);
        logger.LogInformation(
            "Storage cleanup completed: ObjectKeySuffix {ObjectKeySuffix}",
            Path.GetFileName(objectKey));
    }
}
