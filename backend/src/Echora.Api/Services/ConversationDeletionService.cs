using Echora.Api.Entities;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>预览并执行当前用户会话及其整理结果的明确删除传播。</summary>
public sealed class ConversationDeletionService(
    ISqlSugarClient db,
    AttachmentService attachments,
    EmotionSummaryService emotionSummaries,
    ILogger<ConversationDeletionService> logger)
{
    /// <summary>读取二次确认需要展示的删除影响。</summary>
    public async Task<DeletionImpact?> GetImpactAsync(
        long userId,
        long conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = await db.Queryable<Conversation>()
            .Where(item => item.Id == conversationId && item.UserId == userId)
            .FirstAsync(cancellationToken);
        if (conversation is null) return null;
        return new DeletionImpact(
            conversation.Id,
            conversation.Title,
            await db.Queryable<ConversationMessage>().CountAsync(item => item.UserId == userId && item.ConversationId == conversationId, cancellationToken),
            conversation.Summary is null ? 0 : 1,
            await db.Queryable<LifeEvent>().CountAsync(item => item.UserId == userId && item.ConversationId == conversationId, cancellationToken),
            await db.Queryable<Recognition>().CountAsync(item => item.UserId == userId && item.ConversationId == conversationId, cancellationToken),
            await db.Queryable<EmotionRecord>().CountAsync(item => item.UserId == userId && item.ConversationId == conversationId, cancellationToken),
            await db.Queryable<PersonRecord>().CountAsync(item => item.UserId == userId && item.ConversationId == conversationId, cancellationToken),
            await db.Queryable<PlaceRecord>().CountAsync(item => item.UserId == userId && item.ConversationId == conversationId, cancellationToken));
    }

    /// <summary>在一个数据库事务中删除全部会话级业务数据，再删除物理附件。</summary>
    public async Task<bool> DeleteAsync(
        long userId,
        long conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = await db.Queryable<Conversation>()
            .Where(item => item.Id == conversationId && item.UserId == userId)
            .FirstAsync(cancellationToken);
        if (conversation is null) return false;
        var messages = await db.Queryable<ConversationMessage>()
            .Where(item => item.UserId == userId && item.ConversationId == conversationId)
            .ToListAsync(cancellationToken);
        var messageIds = messages.Select(item => item.Id).ToArray();
        var attached = messageIds.Length == 0 ? [] : await db.Queryable<Attachment>().Where(item => item.UserId == userId && item.MessageId != null && messageIds.Contains(item.MessageId.Value)).ToListAsync(cancellationToken);
        var personRecords = await db.Queryable<PersonRecord>().Where(item => item.UserId == userId && item.ConversationId == conversationId).ToListAsync(cancellationToken);
        var placeRecords = await db.Queryable<PlaceRecord>().Where(item => item.UserId == userId && item.ConversationId == conversationId).ToListAsync(cancellationToken);
        var affectedDays = await emotionSummaries.GetConversationDaysAsync(userId, conversationId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        db.Ado.BeginTran();
        try
        {
            await db.Deleteable<AnalysisRun>().Where(item => item.UserId == userId && item.ConversationId == conversationId).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<UnresolvedMention>().Where(item => item.UserId == userId && item.ConversationId == conversationId).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<CbtObservation>().Where(item => item.UserId == userId && item.ConversationId == conversationId).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<EmotionRecord>().Where(item => item.UserId == userId && item.ConversationId == conversationId).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<Recognition>().Where(item => item.UserId == userId && item.ConversationId == conversationId).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<LifeEvent>().Where(item => item.UserId == userId && item.ConversationId == conversationId).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<PersonRecord>().Where(item => item.UserId == userId && item.ConversationId == conversationId).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<PlaceRecord>().Where(item => item.UserId == userId && item.ConversationId == conversationId).ExecuteCommandAsync(cancellationToken);
            if (messageIds.Length > 0)
            {
                await db.Deleteable<PersonAlias>().Where(item => item.UserId == userId && item.SourceMessageId != null && messageIds.Contains(item.SourceMessageId.Value)).ExecuteCommandAsync(cancellationToken);
                await db.Deleteable<PlaceAlias>().Where(item => item.UserId == userId && item.SourceMessageId != null && messageIds.Contains(item.SourceMessageId.Value)).ExecuteCommandAsync(cancellationToken);
            }
            if (attached.Count > 0) await db.Deleteable<Attachment>().Where(item => item.UserId == userId && attached.Select(value => value.Id).Contains(item.Id)).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<ConversationMessage>().Where(item => item.UserId == userId && item.ConversationId == conversationId).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<Conversation>().Where(item => item.UserId == userId && item.Id == conversationId).ExecuteCommandAsync(cancellationToken);

            await RemoveOrphanPeopleAsync(userId, personRecords.Select(item => item.PersonId).Distinct().ToArray(), cancellationToken);
            await RemoveOrphanPlacesAsync(userId, placeRecords.Select(item => item.PlaceId).Distinct().ToArray(), cancellationToken);
            foreach (var day in affectedDays)
                await emotionSummaries.RebuildAsync(userId, day, cancellationToken);

            if (!await db.Queryable<Conversation>()
                    .AnyAsync(item => item.UserId == userId
                        && item.Channel == "Web"
                        && item.Status == "Current", cancellationToken))
            {
                await db.Insertable(new Conversation
                {
                    UserId = userId,
                    Channel = "Web",
                    Title = "新对话",
                    Status = "Current",
                    CreatedAt = now,
                    UpdatedAt = now,
                }).ExecuteCommandAsync(cancellationToken);
            }
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }

        await attachments.DeleteObjectsAsync(attached.SelectMany(AttachmentService.GetObjectKeys));
        logger.LogInformation(
            "Conversation deleted: ConversationId {ConversationId}, MessageCount {MessageCount}, AttachmentCount {AttachmentCount}",
            conversationId,
            messages.Count,
            attached.Count);
        return true;
    }

    /// <summary>删除没有任何会话记录的人物；保留人物则重新选择有效封面。</summary>
    private async Task RemoveOrphanPeopleAsync(long userId, long[] personIds, CancellationToken cancellationToken)
    {
        foreach (var id in personIds)
        {
            var records = await db.Queryable<PersonRecord>().Where(item => item.UserId == userId && item.PersonId == id).OrderBy(item => item.UpdatedAt, OrderByType.Desc).ToListAsync(cancellationToken);
            if (records.Count == 0)
            {
                await db.Deleteable<PersonAlias>().Where(item => item.UserId == userId && item.PersonId == id).ExecuteCommandAsync(cancellationToken);
                await db.Deleteable<Person>().Where(item => item.UserId == userId && item.Id == id).ExecuteCommandAsync(cancellationToken);
                continue;
            }
            var cover = records.SelectMany(item => item.AttachmentIds).FirstOrDefault();
            await db.Updateable<Person>().SetColumns(item => item.CoverAttachmentId == (cover == 0 ? null : cover)).Where(item => item.UserId == userId && item.Id == id).ExecuteCommandAsync(cancellationToken);
        }
    }

    /// <summary>删除没有任何会话记录的地点；保留地点则重新选择有效封面。</summary>
    private async Task RemoveOrphanPlacesAsync(long userId, long[] placeIds, CancellationToken cancellationToken)
    {
        foreach (var id in placeIds)
        {
            var records = await db.Queryable<PlaceRecord>().Where(item => item.UserId == userId && item.PlaceId == id).OrderBy(item => item.UpdatedAt, OrderByType.Desc).ToListAsync(cancellationToken);
            if (records.Count == 0)
            {
                await db.Deleteable<PlaceAlias>().Where(item => item.UserId == userId && item.PlaceId == id).ExecuteCommandAsync(cancellationToken);
                await db.Deleteable<Place>().Where(item => item.UserId == userId && item.Id == id).ExecuteCommandAsync(cancellationToken);
                continue;
            }
            var cover = records.SelectMany(item => item.AttachmentIds).FirstOrDefault();
            await db.Updateable<Place>().SetColumns(item => item.CoverAttachmentId == (cover == 0 ? null : cover)).Where(item => item.UserId == userId && item.Id == id).ExecuteCommandAsync(cancellationToken);
        }
    }

    /// <summary>删除前展示的业务影响。</summary>
    public sealed record DeletionImpact(long ConversationId, string Title, int MessageCount, int FragmentCount, int EventCount, int RecognitionCount, int EmotionCount, int PersonRecordCount, int PlaceRecordCount);
}
