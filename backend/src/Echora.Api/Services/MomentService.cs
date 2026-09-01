using Echora.Api.Contracts;
using Echora.Api.Entities;
using Echora.Api.Jobs;
using Hangfire;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>发布、读取、重试和删除一刻。</summary>
public sealed class MomentService(
    ISqlSugarClient db,
    AttachmentService attachments,
    IBackgroundJobClient jobs,
    DayDigestService dayDigests,
    ILogger<MomentService> logger)
{
    /// <summary>发布原始一刻并分别入队 MomentAgent 和三分支分析。</summary>
    public async Task<MomentResponse> CreateAsync(
        long userId,
        CreateMomentRequest request,
        CancellationToken cancellationToken)
    {
        var attachment = await db.Queryable<Attachment>()
            .Where(item => item.Id == request.AttachmentId && item.UserId == userId)
            .FirstAsync(cancellationToken);
        if (attachment is null || !attachment.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择已经上传的图片。");
        if (attachment.MessageId is not null
            || attachment.MomentId is not null
            || await db.Queryable<Moment>()
                .AnyAsync(item => item.UserId == userId && item.AttachmentId == attachment.Id, cancellationToken))
            throw new ArgumentException("图片已经被其他记录使用。");
        var text = request.Text?.Trim();
        if (text?.Length > 5000) throw new ArgumentException("一刻描述不能超过 5000 个字符。");
        var now = DateTimeOffset.UtcNow;
        var moment = new Moment
        {
            UserId = userId,
            AttachmentId = attachment.Id,
            Text = string.IsNullOrWhiteSpace(text) ? null : text,
            CapturedAt = attachment.CapturedAt ?? now,
            PublishedAt = now,
            Latitude = attachment.Latitude,
            Longitude = attachment.Longitude,
            Status = "Pending",
            UpdatedAt = now,
        };
        long analysisRunId;
        db.Ado.BeginTran();
        try
        {
            moment.Id = await db.Insertable(moment).ExecuteReturnBigIdentityAsync(cancellationToken);
            analysisRunId = await db.Insertable(new AnalysisRun
            {
                UserId = userId,
                MomentId = moment.Id,
                CreatedAt = now,
                UpdatedAt = now,
            }).ExecuteReturnBigIdentityAsync(cancellationToken);
            attachment.MomentId = moment.Id;
            await db.Updateable(attachment).ExecuteCommandAsync(cancellationToken);
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
        try
        {
            moment.HangfireJobId = jobs.Enqueue<MomentJob>(job => job.ExecuteAsync(moment.Id, CancellationToken.None));
            var analysisJobId = jobs.Enqueue<AnalysisJob>(job => job.ExecuteAsync(analysisRunId, CancellationToken.None));
            await db.Updateable(moment).ExecuteCommandAsync(CancellationToken.None);
            await db.Updateable<AnalysisRun>()
                .SetColumns(item => item.HangfireJobId == analysisJobId)
                .Where(item => item.Id == analysisRunId)
                .ExecuteCommandAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Moment enqueue failed: MomentId {MomentId}, AnalysisRunId {AnalysisRunId}", moment.Id, analysisRunId);
        }
        return ToResponse(moment, attachment);
    }

    /// <summary>按发布时间倒序读取全部一刻。</summary>
    public async Task<IReadOnlyList<MomentResponse>> GetAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        var moments = await db.Queryable<Moment>()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.PublishedAt, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        var ids = moments.Select(item => item.AttachmentId).ToArray();
        var files = ids.Length == 0
            ? []
            : await db.Queryable<Attachment>()
                .Where(item => item.UserId == userId && ids.Contains(item.Id))
                .ToListAsync(cancellationToken);
        var filesById = files.ToDictionary(item => item.Id);
        return moments
            .Where(item => filesById.ContainsKey(item.AttachmentId))
            .Select(item => ToResponse(item, filesById[item.AttachmentId]))
            .ToArray();
    }

    /// <summary>重新入队失败的一刻整理或三分支分析。</summary>
    public async Task<bool> RetryAsync(long userId, long id, CancellationToken cancellationToken)
    {
        var moment = await db.Queryable<Moment>()
            .Where(item => item.Id == id && item.UserId == userId)
            .FirstAsync(cancellationToken);
        if (moment is null) return false;
        if (moment.Status == "Failed")
        {
            moment.Status = "Pending";
            moment.ErrorMessage = null;
            moment.HangfireJobId = jobs.Enqueue<MomentJob>(job => job.ExecuteAsync(id, CancellationToken.None));
            moment.UpdatedAt = DateTimeOffset.UtcNow;
            await db.Updateable(moment).ExecuteCommandAsync(cancellationToken);
        }
        var run = await db.Queryable<AnalysisRun>()
            .Where(item => item.UserId == userId && item.MomentId == id)
            .FirstAsync(cancellationToken);
        if (run is not null && (run.LifeRecordStatus == "Failed" || run.RecognitionStatus == "Failed" || run.EmotionStatus == "Failed"))
        {
            if (run.LifeRecordStatus == "Failed") { run.LifeRecordStatus = "Pending"; run.LifeRecordError = null; }
            if (run.RecognitionStatus == "Failed") { run.RecognitionStatus = "Pending"; run.RecognitionError = null; }
            if (run.EmotionStatus == "Failed") { run.EmotionStatus = "Pending"; run.EmotionError = null; }
            run.HangfireJobId = jobs.Enqueue<AnalysisJob>(job => job.ExecuteAsync(run.Id, CancellationToken.None));
            run.UpdatedAt = DateTimeOffset.UtcNow;
            await db.Updateable(run).ExecuteCommandAsync(cancellationToken);
        }
        return true;
    }

    /// <summary>删除一刻及其全部派生记录，事务提交后删除图片文件。</summary>
    public async Task<bool> DeleteAsync(long userId, long id, CancellationToken cancellationToken)
    {
        var moment = await db.Queryable<Moment>()
            .Where(item => item.Id == id && item.UserId == userId)
            .FirstAsync(cancellationToken);
        if (moment is null) return false;
        var attachment = await db.Queryable<Attachment>()
            .Where(item => item.Id == moment.AttachmentId && item.UserId == userId)
            .FirstAsync(cancellationToken);
        var personIds = (await db.Queryable<PersonRecord>()
            .Where(item => item.UserId == userId && item.SourceMomentId == id)
            .Select(item => item.PersonId)
            .ToListAsync(cancellationToken)).Distinct().ToArray();
        var placeIds = (await db.Queryable<PlaceRecord>()
            .Where(item => item.UserId == userId && item.SourceMomentId == id)
            .Select(item => item.PlaceId)
            .ToListAsync(cancellationToken)).Distinct().ToArray();
        db.Ado.BeginTran();
        try
        {
            await db.Deleteable<AnalysisRun>().Where(item => item.UserId == userId && item.MomentId == id).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<UnresolvedMention>().Where(item => item.UserId == userId && item.SourceMomentId == id).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<CbtObservation>().Where(item => item.UserId == userId && item.SourceMomentId == id).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<EmotionRecord>().Where(item => item.UserId == userId && item.SourceMomentId == id).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<Recognition>().Where(item => item.UserId == userId && item.SourceMomentId == id).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<LifeEvent>().Where(item => item.UserId == userId && item.SourceMomentId == id).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<PersonRecord>().Where(item => item.UserId == userId && item.SourceMomentId == id).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<PlaceRecord>().Where(item => item.UserId == userId && item.SourceMomentId == id).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<PersonAlias>().Where(item => item.UserId == userId && item.SourceMomentId == id).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<PlaceAlias>().Where(item => item.UserId == userId && item.SourceMomentId == id).ExecuteCommandAsync(cancellationToken);
            await RemoveOrphanPeopleAsync(userId, personIds, cancellationToken);
            await RemoveOrphanPlacesAsync(userId, placeIds, cancellationToken);
            await db.Deleteable<Moment>()
                .Where(item => item.Id == id && item.UserId == userId)
                .ExecuteCommandAsync(cancellationToken);
            if (attachment is not null)
                await db.Deleteable<Attachment>()
                    .Where(item => item.Id == attachment.Id && item.UserId == userId)
                    .ExecuteCommandAsync(cancellationToken);
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
        // 摘要里可能引用了这条一刻的原话，先删掉，避免页面继续展示已删除的内容。
        await dayDigests.RemoveAsync(
            userId,
            [DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment.PublishedAt, ShanghaiTimeZone).DateTime)],
            cancellationToken);
        if (attachment is not null)
            await attachments.DeleteObjectsAsync(AttachmentService.GetObjectKeys(attachment));
        return true;
    }

    /// <summary>删除失去全部来源的人物，或为仍存在的人物重新选择封面。</summary>
    private async Task RemoveOrphanPeopleAsync(long userId, long[] ids, CancellationToken cancellationToken)
    {
        foreach (var id in ids)
        {
            var records = await db.Queryable<PersonRecord>()
                .Where(item => item.UserId == userId && item.PersonId == id)
                .OrderBy(item => item.UpdatedAt, OrderByType.Desc)
                .ToListAsync(cancellationToken);
            if (records.Count == 0)
            {
                await db.Deleteable<PersonAlias>().Where(item => item.UserId == userId && item.PersonId == id).ExecuteCommandAsync(cancellationToken);
                await db.Deleteable<Person>().Where(item => item.UserId == userId && item.Id == id).ExecuteCommandAsync(cancellationToken);
                continue;
            }
            var cover = records.SelectMany(item => item.AttachmentIds).FirstOrDefault();
            await db.Updateable<Person>()
                .SetColumns(item => item.CoverAttachmentId == (cover == 0 ? null : cover))
                .Where(item => item.UserId == userId && item.Id == id)
                .ExecuteCommandAsync(cancellationToken);
        }
    }

    /// <summary>删除失去全部来源的地点，或为仍存在的地点重新选择封面。</summary>
    private async Task RemoveOrphanPlacesAsync(long userId, long[] ids, CancellationToken cancellationToken)
    {
        foreach (var id in ids)
        {
            var records = await db.Queryable<PlaceRecord>()
                .Where(item => item.UserId == userId && item.PlaceId == id)
                .OrderBy(item => item.UpdatedAt, OrderByType.Desc)
                .ToListAsync(cancellationToken);
            if (records.Count == 0)
            {
                await db.Deleteable<PlaceAlias>().Where(item => item.UserId == userId && item.PlaceId == id).ExecuteCommandAsync(cancellationToken);
                await db.Deleteable<Place>().Where(item => item.UserId == userId && item.Id == id).ExecuteCommandAsync(cancellationToken);
                continue;
            }
            var cover = records.SelectMany(item => item.AttachmentIds).FirstOrDefault();
            await db.Updateable<Place>()
                .SetColumns(item => item.CoverAttachmentId == (cover == 0 ? null : cover))
                .Where(item => item.UserId == userId && item.Id == id)
                .ExecuteCommandAsync(cancellationToken);
        }
    }

    private static readonly TimeZoneInfo ShanghaiTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    private static MomentResponse ToResponse(Moment moment, Attachment attachment) => new(
        moment.Id,
        moment.AttachmentId,
        moment.Text,
        moment.Title,
        moment.Summary,
        moment.Keywords,
        moment.CapturedAt,
        moment.PublishedAt,
        moment.LocationName,
        moment.LocationAddress,
        moment.Province,
        moment.City,
        moment.Status,
        moment.ErrorMessage,
        $"/api/assets/{attachment.Id}/content",
        attachment.AiDescription);
}
