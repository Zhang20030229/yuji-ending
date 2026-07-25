using Echora.Api.Agents;
using Echora.Api.Entities;
using Echora.Api.Services;
using Hangfire;
using Microsoft.Extensions.AI;
using Echora.Api.Realtime;
using SqlSugar;

namespace Echora.Api.Jobs;

/// <summary>独立运行 MomentAgent，只更新一刻自身。</summary>
public sealed class MomentJob(
    ISqlSugarClient db,
    AttachmentService attachments,
    MomentAgent agent,
    DataUpdateNotifier notifier,
    ILogger<MomentJob> logger)
{
    /// <summary>瞬时失败最多自动重试两次。</summary>
    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(long momentId, CancellationToken cancellationToken)
    {
        var moment = await db.Queryable<Moment>().Where(item => item.Id == momentId).FirstAsync(cancellationToken);
        if (moment is null || moment.Status == "Succeeded") return;
        var attachment = await db.Queryable<Attachment>()
            .Where(item => item.Id == moment.AttachmentId && item.UserId == moment.UserId)
            .FirstAsync(cancellationToken)
            ?? throw new InvalidOperationException("一刻照片不存在。");
        moment.Status = "Running";
        moment.AttemptCount++;
        moment.ErrorMessage = null;
        moment.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(moment).ExecuteCommandAsync(cancellationToken);
        await notifier.NotifyAsync(moment.UserId, "running", cancellationToken, "analysis", "moments");
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        logger.LogInformation("Moment Agent started: MomentId {MomentId}, AttachmentId {AttachmentId}", moment.Id, attachment.Id);
        try
        {
            var location = string.Join(' ', new[] { moment.LocationName, moment.LocationAddress, moment.Province, moment.City }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var text = string.IsNullOrWhiteSpace(moment.Text) ? "用户没有填写文字描述。" : moment.Text.Trim();
            var prompt = $"{text}\n拍摄时间：{moment.CapturedAt:O}\n发布时间：{moment.PublishedAt:O}"
                + (location.Length > 0 ? $"\n设备记录位置：{location}" : string.Empty);
            var message = new ChatMessage(ChatRole.User,
            [
                new TextContent(prompt),
                new DataContent(
                    await attachments.ReadBytesAsync(attachment, cancellationToken),
                    AttachmentService.GetReadableMimeType(attachment)),
            ]);
            var result = await agent.RunAsync(new MomentAgentInput(moment.UserId, message), cancellationToken);
            moment.Title = result.Title;
            moment.Summary = result.Summary;
            moment.Keywords = result.Keywords;
            moment.Status = "Succeeded";
            moment.DurationMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            moment.UpdatedAt = DateTimeOffset.UtcNow;
            attachment.AiDescription = result.ImageDescription;
            db.Ado.BeginTran();
            try
            {
                await db.Updateable(moment).ExecuteCommandAsync(cancellationToken);
                await db.Updateable(attachment).ExecuteCommandAsync(cancellationToken);
                db.Ado.CommitTran();
            }
            catch
            {
                db.Ado.RollbackTran();
                throw;
            }
            logger.LogInformation("Moment Agent completed: MomentId {MomentId}, DurationMs {DurationMs}", moment.Id, moment.DurationMs);
            await notifier.NotifyAsync(moment.UserId, "settled", cancellationToken, "analysis", "moments", "archive", "self");
        }
        catch (Exception exception)
        {
            moment.Status = "Failed";
            moment.ErrorMessage = exception is InvalidDataException or InvalidOperationException ? exception.Message : "一刻整理失败。";
            moment.DurationMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            moment.UpdatedAt = DateTimeOffset.UtcNow;
            await db.Updateable(moment).ExecuteCommandAsync(CancellationToken.None);
            logger.LogError(exception, "Moment Agent failed: MomentId {MomentId}", moment.Id);
            await notifier.NotifyAsync(moment.UserId, "settled", CancellationToken.None, "analysis", "moments", "archive", "self");
            throw;
        }
    }
}
