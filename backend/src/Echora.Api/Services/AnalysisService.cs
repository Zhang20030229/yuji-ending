using System.Text.Json;
using Echora.Api.Agents;
using Echora.Api.Entities;
using Echora.Api.Workflows;
using Microsoft.Extensions.AI;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>构建单目标分析输入，并按来源幂等保存三个分支。</summary>
public sealed class AnalysisService(
    ISqlSugarClient db,
    AttachmentService attachments,
    ILogger<AnalysisService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>从数据库恢复完整上下文，并把当前目标放在最后一条 User Message。</summary>
    public async Task<AnalysisInput?> BuildInputAsync(
        AnalysisRun run,
        string[] branches,
        CancellationToken cancellationToken)
    {
        if (run.TargetMessageId is long messageId)
            return await BuildMessageInputAsync(run, messageId, branches, cancellationToken);
        if (run.MomentId is long momentId)
            return await BuildMomentInputAsync(run, momentId, branches, cancellationToken);
        return null;
    }

    /// <summary>保存一个成功分支；仅替换相同来源的同类结果。</summary>
    public async Task SaveBranchAsync(
        AnalysisRun run,
        AnalysisBranchResult branch,
        AnalysisInput input,
        CancellationToken cancellationToken)
    {
        if (!branch.Succeeded || string.IsNullOrWhiteSpace(branch.Json))
            throw new InvalidOperationException("失败分支不能进入业务保存。");
        switch (branch.Branch)
        {
            case "LifeRecord":
                await SaveLifeRecordAsync(run, input, branch.Json, cancellationToken);
                break;
            case "Recognition":
                await SaveRecognitionsAsync(run, input, branch.Json, cancellationToken);
                break;
            case "Emotion":
                await SaveEmotionsAsync(run, input, branch.Json, cancellationToken);
                break;
            default:
                throw new InvalidDataException("未知分析分支。");
        }
        logger.LogInformation(
            "Analysis branch saved: AnalysisRunId {AnalysisRunId}, Branch {Branch}, TargetMessageId {TargetMessageId}, MomentId {MomentId}",
            run.Id,
            branch.Branch,
            run.TargetMessageId,
            run.MomentId);
    }

    /// <summary>构造聊天来源输入；目标之后的 Assistant 回复不参与后台事实分析。</summary>
    private async Task<AnalysisInput?> BuildMessageInputAsync(
        AnalysisRun run,
        long messageId,
        string[] branches,
        CancellationToken cancellationToken)
    {
        var target = await db.Queryable<ConversationMessage>()
            .Where(item => item.Id == messageId
                && item.UserId == run.UserId
                && item.Role == "User"
                && item.Status == "Completed")
            .FirstAsync(cancellationToken);
        if (target is null || target.ConversationId != run.ConversationId) return null;
        var conversation = await db.Queryable<Conversation>()
            .Where(item => item.Id == target.ConversationId && item.UserId == run.UserId)
            .FirstAsync(cancellationToken);
        if (conversation is null) return null;
        var user = await db.Queryable<UserAccount>()
            .Where(item => item.Id == run.UserId)
            .FirstAsync(cancellationToken);
        if (user is null) return null;
        var rows = await db.Queryable<ConversationMessage>()
            .Where(item => item.ConversationId == target.ConversationId
                && item.UserId == run.UserId
                && item.Sequence <= target.Sequence
                && item.Status == "Completed"
                && item.ContentJson != null)
            .OrderBy(item => item.Sequence)
            .ToListAsync(cancellationToken);
        var rowIds = rows.Select(item => item.Id).ToArray();
        var assetRows = rowIds.Length == 0
            ? []
            : await db.Queryable<Attachment>()
                .Where(item => item.UserId == run.UserId
                    && item.MessageId != null
                    && rowIds.Contains(item.MessageId.Value))
                .ToListAsync(cancellationToken);
        var assetsByMessage = assetRows.GroupBy(item => item.MessageId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Id).ToArray());
        var messages = new List<ChatMessage>(rows.Count + 1);
        foreach (var row in rows.Where(item => item.Id != target.Id))
        {
            var historical = await RestoreMessageAsync(
                row,
                assetsByMessage.GetValueOrDefault(row.Id) ?? [],
                false,
                cancellationToken);
            if (historical.Role == ChatRole.User || historical.Role == ChatRole.Assistant)
                historical.Contents.Insert(0, new TextContent("【历史上下文，不生成记录】"));
            messages.Add(historical);
        }
        var targetBoundary = "下一条 User Message 是本次唯一分析目标。更早的消息和查询结果只用于理解上下文，"
            + "不得重复输出其中的人物、地点、事件、认识或情绪；只有 fragment 可以结合已有总结累计更新。";
        if (!string.IsNullOrWhiteSpace(conversation.Summary))
            targetBoundary += $"\n当前会话已有片段总结：{conversation.Summary}";
        messages.Add(new ChatMessage(ChatRole.System, targetBoundary));
        var targetAssets = assetsByMessage.GetValueOrDefault(target.Id) ?? [];
        var targetMessage = await RestoreMessageAsync(target, targetAssets, true, cancellationToken);
        targetMessage.Contents.Insert(0, new TextContent("【本次唯一分析目标】"));
        messages.Add(targetMessage);
        var explicitText = string.Join('\n',
            ChatMessageJson.Deserialize(target.ContentJson!).Contents
                .OfType<TextContent>()
                .Select(content => content.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        return new AnalysisInput(
            run.Id,
            run.UserId,
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
            user.AiName,
            target.ConversationId,
            target.Id,
            null,
            target.Sequence,
            messages.ToArray(),
            targetAssets.Select(item => item.Id).ToArray(),
            target.CreatedAt,
            branches)
        {
            ExplicitText = explicitText,
        };
    }

    /// <summary>构造一刻来源输入；模型只看到当前照片、原话、时间和位置。</summary>
    private async Task<AnalysisInput?> BuildMomentInputAsync(
        AnalysisRun run,
        long momentId,
        string[] branches,
        CancellationToken cancellationToken)
    {
        var moment = await db.Queryable<Moment>()
            .Where(item => item.Id == momentId && item.UserId == run.UserId)
            .FirstAsync(cancellationToken);
        if (moment is null) return null;
        var user = await db.Queryable<UserAccount>()
            .Where(item => item.Id == run.UserId)
            .FirstAsync(cancellationToken);
        if (user is null) return null;
        var attachment = await db.Queryable<Attachment>()
            .Where(item => item.Id == moment.AttachmentId && item.UserId == run.UserId)
            .FirstAsync(cancellationToken);
        if (attachment is null) return null;
        var location = string.Join(' ', new[] { moment.LocationName, moment.LocationAddress, moment.Province, moment.City }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var text = string.IsNullOrWhiteSpace(moment.Text) ? "用户没有填写文字描述。" : moment.Text.Trim();
        var context = $"{text}\n拍摄时间：{moment.CapturedAt:O}\n发布时间：{moment.PublishedAt:O}"
            + (location.Length > 0 ? $"\n设备记录位置：{location}" : string.Empty);
        var message = new ChatMessage(ChatRole.User,
        [
            new TextContent(context),
            new TextContent("【图片1】"),
            new DataContent(
                await attachments.ReadBytesAsync(attachment, cancellationToken),
                AttachmentService.GetReadableMimeType(attachment)),
        ]);
        return new AnalysisInput(
            run.Id,
            run.UserId,
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
            user.AiName,
            null,
            null,
            moment.Id,
            0,
            [message],
            [attachment.Id],
            moment.PublishedAt,
            branches)
        {
            HasExplicitText = !string.IsNullOrWhiteSpace(moment.Text),
            ExplicitText = moment.Text?.Trim() ?? string.Empty,
        };
    }

    /// <summary>恢复一条 MAF 消息，并把私有附件 URI 替换为真实字节。</summary>
    private async Task<ChatMessage> RestoreMessageAsync(
        ConversationMessage row,
        IReadOnlyList<Attachment> rowAttachments,
        bool includeLocation,
        CancellationToken cancellationToken)
    {
        var restored = ChatMessageJson.Deserialize(row.ContentJson!);
        var byId = rowAttachments.ToDictionary(item => item.Id);
        var contents = new List<AIContent>();
        var imageIndex = 0;
        foreach (var content in restored.Contents)
        {
            if (content is not UriContent uri || uri.Uri.Scheme != "echora-attachment")
            {
                contents.Add(content);
                continue;
            }
            var rawId = uri.Uri.OriginalString[(uri.Uri.OriginalString.IndexOf(':') + 1)..].Trim('/');
            if (!long.TryParse(rawId, out var attachmentId) || !byId.TryGetValue(attachmentId, out var attachment))
                throw new InvalidDataException("聊天消息引用的附件不存在。");
            if (includeLocation) contents.Add(new TextContent($"【图片{++imageIndex}】"));
            contents.Add(new DataContent(
                await attachments.ReadBytesAsync(attachment, cancellationToken),
                AttachmentService.GetReadableMimeType(attachment)));
        }
        if (includeLocation)
        {
            var location = string.Join(' ', new[] { row.LocationName, row.LocationAddress, row.Province, row.City }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            if (location.Length > 0) contents.Add(new TextContent($"设备记录位置：{location}"));
        }
        return new ChatMessage(restored.Role, contents) { CreatedAt = row.CreatedAt };
    }

    /// <summary>幂等保存片段、人物、地点、事件、图片说明和待确认项。</summary>
    private async Task SaveLifeRecordAsync(
        AnalysisRun run,
        AnalysisInput input,
        string json,
        CancellationToken cancellationToken)
    {
        var result = JsonSerializer.Deserialize<LifeRecordSubagent.Result>(json, JsonOptions)
            ?? throw new InvalidDataException("生活记录结果为空。");
        var now = DateTimeOffset.UtcNow;
        var sourceAttachments = input.AttachmentIds.Length == 0
            ? []
            : await db.Queryable<Attachment>()
                .Where(item => item.UserId == run.UserId && input.AttachmentIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
        var attachmentById = sourceAttachments.ToDictionary(item => item.Id);
        long[] SelectAttachments(IEnumerable<int>? indexes) =>
            (indexes ?? [])
                .Where(index => index > 0 && index <= input.AttachmentIds.Length)
                .Select(index => input.AttachmentIds[index - 1])
                .Where(attachmentById.ContainsKey)
                .Distinct()
                .ToArray();
        var affectedPersonIds = await SourceQuery<PersonRecord>(run).Select(item => item.PersonId).ToListAsync(cancellationToken);
        var affectedPlaceIds = await SourceQuery<PlaceRecord>(run).Select(item => item.PlaceId).ToListAsync(cancellationToken);
        db.Ado.BeginTran();
        try
        {
            await DeleteLifeSourceAsync(run, cancellationToken);
            if (run.ConversationId is long conversationId
                && !string.IsNullOrWhiteSpace(result.Fragment.Title)
                && !string.IsNullOrWhiteSpace(result.Fragment.Summary))
            {
                var conversation = await db.Queryable<Conversation>()
                    .Where(item => item.Id == conversationId && item.UserId == run.UserId)
                    .FirstAsync(cancellationToken)
                    ?? throw new InvalidOperationException("会话已经删除。");
                if (input.TargetSequence > conversation.SummaryThroughSequence)
                {
                    conversation.Summary = result.Fragment.Summary;
                    conversation.SummaryThroughSequence = input.TargetSequence;
                    if (!conversation.IsTitleEdited) conversation.Title = result.Fragment.Title;
                    conversation.UpdatedAt = now;
                    await db.Updateable(conversation).ExecuteCommandAsync(cancellationToken);
                }
            }

            var personIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in result.People ?? [])
            {
                var itemAttachments = SelectAttachments(item.ImageIndexes);
                var id = await ResolvePersonAsync(item, run, itemAttachments, now, cancellationToken);
                if (id is null)
                {
                    await InsertUnresolvedAsync(run, "Person", item.Name, "存在多个匹配人物，请手动选择。", itemAttachments, now, cancellationToken);
                    continue;
                }
                personIds[item.Name] = id.Value;
                foreach (var alias in item.Aliases) personIds[alias] = id.Value;
                await db.Insertable(new PersonRecord
                {
                    UserId = run.UserId,
                    PersonId = id.Value,
                    ConversationId = run.ConversationId,
                    Summary = item.Summary,
                    SourceMessageId = run.TargetMessageId,
                    SourceMomentId = run.MomentId,
                    AttachmentIds = itemAttachments,
                    CreatedAt = now,
                    UpdatedAt = now,
                }).ExecuteCommandAsync(cancellationToken);
            }

            var placeIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in result.Places ?? [])
            {
                var itemAttachments = SelectAttachments(item.ImageIndexes);
                var coordinate = FirstCoordinate(itemAttachments, attachmentById);
                // 兜底：AI 未关联图片或关联的图片无坐标时，用本次来源中任意带坐标的附件，
                // 避免地点因 AI 漏关联而落不到地图上。
                if (coordinate.Latitude is null)
                    coordinate = FirstCoordinate(input.AttachmentIds, attachmentById);
                var id = await ResolvePlaceAsync(
                    item,
                    run,
                    itemAttachments,
                    coordinate.Latitude,
                    coordinate.Longitude,
                    now,
                    cancellationToken);
                if (id is null)
                {
                    await InsertUnresolvedAsync(run, "Place", item.Name, "存在多个匹配地点，请手动选择。", itemAttachments, now, cancellationToken);
                    continue;
                }
                placeIds[item.Name] = id.Value;
                foreach (var alias in item.Aliases) placeIds[alias] = id.Value;
                await db.Insertable(new PlaceRecord
                {
                    UserId = run.UserId,
                    PlaceId = id.Value,
                    ConversationId = run.ConversationId,
                    Summary = item.Summary,
                    SourceMessageId = run.TargetMessageId,
                    SourceMomentId = run.MomentId,
                    AttachmentIds = itemAttachments,
                    CreatedAt = now,
                    UpdatedAt = now,
                }).ExecuteCommandAsync(cancellationToken);
            }

            foreach (var item in result.Events ?? [])
            {
                var itemAttachments = SelectAttachments(item.ImageIndexes);
                var eventPeople = await ResolvePersonNamesAsync(
                    item.PersonNames,
                    personIds,
                    run.UserId,
                    cancellationToken);
                var eventPlaces = await ResolvePlaceNamesAsync(
                    item.PlaceNames,
                    placeIds,
                    run.UserId,
                    cancellationToken);
                await db.Insertable(new LifeEvent
                {
                    UserId = run.UserId,
                    ConversationId = run.ConversationId,
                    Title = item.Title,
                    Summary = item.Summary,
                    OccurredAt = input.OccurredAt,
                    PersonIds = eventPeople,
                    PlaceIds = eventPlaces,
                    AttachmentIds = itemAttachments,
                    SourceMessageId = run.TargetMessageId,
                    SourceMomentId = run.MomentId,
                    CreatedAt = now,
                    UpdatedAt = now,
                }).ExecuteCommandAsync(cancellationToken);
            }
            foreach (var item in result.UnresolvedMentions ?? [])
                await InsertUnresolvedAsync(
                    run,
                    item.Kind == "person" ? "Person" : "Place",
                    item.Mention,
                    item.Reason,
                    SelectAttachments(item.ImageIndexes),
                    now,
                    cancellationToken);
            for (var index = 0; index < Math.Min(input.AttachmentIds.Length, result.AttachmentDescriptions?.Length ?? 0); index++)
            {
                var description = result.AttachmentDescriptions![index];
                await db.Updateable<Attachment>()
                    .SetColumns(item => item.AiDescription == description)
                    .Where(item => item.Id == input.AttachmentIds[index] && item.UserId == run.UserId)
                    .ExecuteCommandAsync(cancellationToken);
            }
            await RemoveOrphanPeopleAsync(run.UserId, affectedPersonIds.Distinct().ToArray(), cancellationToken);
            await RemoveOrphanPlacesAsync(run.UserId, affectedPlaceIds.Distinct().ToArray(), cancellationToken);
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    /// <summary>幂等保存当前来源新增的认识。</summary>
    private async Task SaveRecognitionsAsync(
        AnalysisRun run,
        AnalysisInput input,
        string json,
        CancellationToken cancellationToken)
    {
        var result = JsonSerializer.Deserialize<RecognitionSubagent.Result>(json, JsonOptions)
            ?? throw new InvalidDataException("认识结果为空。");
        db.Ado.BeginTran();
        try
        {
            await DeleteSourceAsync<Recognition>(run, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            foreach (var item in result.Recognitions ?? [])
                await db.Insertable(new Recognition
                {
                    UserId = run.UserId,
                    ConversationId = run.ConversationId,
                    Category = item.Category,
                    Content = item.Content,
                    Keywords = item.Keywords,
                    SourceMessageId = run.TargetMessageId,
                    SourceMomentId = run.MomentId,
                    CreatedAt = now,
                    UpdatedAt = now,
                }).ExecuteCommandAsync(cancellationToken);
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    /// <summary>幂等保存当前来源新增的情绪。</summary>
    private async Task SaveEmotionsAsync(
        AnalysisRun run,
        AnalysisInput input,
        string json,
        CancellationToken cancellationToken)
    {
        var result = JsonSerializer.Deserialize<EmotionSubagent.Result>(json, JsonOptions)
            ?? throw new InvalidDataException("情绪结果为空。");
        db.Ado.BeginTran();
        try
        {
            await DeleteSourceAsync<EmotionRecord>(run, cancellationToken);
            await DeleteSourceAsync<CbtObservation>(run, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            foreach (var item in result.Emotions ?? [])
                await db.Insertable(new EmotionRecord
                {
                    UserId = run.UserId,
                    ConversationId = run.ConversationId,
                    Family = item.Family,
                    Subtype = item.Subtype,
                    Intensity = item.Intensity,
                    Summary = item.Summary,
                    OccurredAt = input.OccurredAt,
                    SourceMessageId = run.TargetMessageId,
                    SourceMomentId = run.MomentId,
                    CreatedAt = now,
                    UpdatedAt = now,
                }).ExecuteCommandAsync(cancellationToken);
            if (result.CbtObservation is not null)
                await db.Insertable(new CbtObservation
                {
                    UserId = run.UserId,
                    ConversationId = run.ConversationId,
                    Situation = result.CbtObservation.Situation,
                    AutomaticThought = result.CbtObservation.AutomaticThought,
                    BodySensation = result.CbtObservation.BodySensation,
                    Behavior = result.CbtObservation.Behavior,
                    ImmediateOutcome = result.CbtObservation.ImmediateOutcome,
                    OccurredAt = input.OccurredAt,
                    SourceMessageId = run.TargetMessageId,
                    SourceMomentId = run.MomentId,
                    CreatedAt = now,
                    UpdatedAt = now,
                }).ExecuteCommandAsync(cancellationToken);
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    /// <summary>按当前来源删除生活分支上一次结果和别名。</summary>
    private async Task DeleteLifeSourceAsync(AnalysisRun run, CancellationToken cancellationToken)
    {
        await DeleteSourceAsync<PersonRecord>(run, cancellationToken);
        await DeleteSourceAsync<PlaceRecord>(run, cancellationToken);
        await DeleteSourceAsync<LifeEvent>(run, cancellationToken);
        await DeleteSourceAsync<UnresolvedMention>(run, cancellationToken);
        if (run.TargetMessageId is long messageId)
        {
            await db.Deleteable<PersonAlias>()
                .Where(item => item.UserId == run.UserId && item.SourceMessageId == messageId)
                .ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<PlaceAlias>()
                .Where(item => item.UserId == run.UserId && item.SourceMessageId == messageId)
                .ExecuteCommandAsync(cancellationToken);
        }
        else if (run.MomentId is long momentId)
        {
            await db.Deleteable<PersonAlias>()
                .Where(item => item.UserId == run.UserId && item.SourceMomentId == momentId)
                .ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<PlaceAlias>()
                .Where(item => item.UserId == run.UserId && item.SourceMomentId == momentId)
                .ExecuteCommandAsync(cancellationToken);
        }
    }

    /// <summary>按明确的聊天或一刻来源删除指定派生表记录。</summary>
    private async Task DeleteSourceAsync<TEntity>(AnalysisRun run, CancellationToken cancellationToken) where TEntity : class, new()
    {
        var delete = db.Deleteable<TEntity>();
        if (run.TargetMessageId is long messageId)
            await delete.Where("user_id = @userId AND source_message_id = @messageId", new { userId = run.UserId, messageId }).ExecuteCommandAsync(cancellationToken);
        else if (run.MomentId is long momentId)
            await delete.Where("user_id = @userId AND source_moment_id = @momentId", new { userId = run.UserId, momentId }).ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>构造当前聊天消息或一刻来源的读取条件。</summary>
    private ISugarQueryable<TEntity> SourceQuery<TEntity>(AnalysisRun run) where TEntity : class, new()
    {
        var query = db.Queryable<TEntity>();
        if (run.TargetMessageId is long messageId)
            return query.Where("user_id = @userId AND source_message_id = @messageId", new { userId = run.UserId, messageId });
        if (run.MomentId is long momentId)
            return query.Where("user_id = @userId AND source_moment_id = @momentId", new { userId = run.UserId, momentId });
        return query.Where("1 = 0");
    }

    /// <summary>删除当前来源重试后不再有任何记录的人物。</summary>
    private async Task RemoveOrphanPeopleAsync(
        long userId,
        long[] ids,
        CancellationToken cancellationToken)
    {
        foreach (var id in ids)
        {
            if (await db.Queryable<PersonRecord>()
                    .AnyAsync(item => item.UserId == userId && item.PersonId == id, cancellationToken))
                continue;
            await db.Deleteable<PersonAlias>()
                .Where(item => item.UserId == userId && item.PersonId == id)
                .ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<Person>()
                .Where(item => item.UserId == userId && item.Id == id)
                .ExecuteCommandAsync(cancellationToken);
        }
    }

    /// <summary>删除当前来源重试后不再有任何记录的地点。</summary>
    private async Task RemoveOrphanPlacesAsync(
        long userId,
        long[] ids,
        CancellationToken cancellationToken)
    {
        foreach (var id in ids)
        {
            if (await db.Queryable<PlaceRecord>()
                    .AnyAsync(item => item.UserId == userId && item.PlaceId == id, cancellationToken))
                continue;
            await db.Deleteable<PlaceAlias>()
                .Where(item => item.UserId == userId && item.PlaceId == id)
                .ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<Place>()
                .Where(item => item.UserId == userId && item.Id == id)
                .ExecuteCommandAsync(cancellationToken);
        }
    }

    /// <summary>按名称和已确认别名解析人物；无匹配时创建主卡。</summary>
    private async Task<long?> ResolvePersonAsync(
        LifeRecordSubagent.PersonItem item,
        AnalysisRun run,
        long[] attachmentIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(item.Name);
        var candidates = (await db.Queryable<Person>()
                .Where(row => row.UserId == run.UserId && row.NormalizedName == normalized)
                .Select(row => row.Id)
                .ToListAsync(cancellationToken))
            .Concat(await db.Queryable<PersonAlias>()
                .Where(row => row.UserId == run.UserId && row.NormalizedName == normalized)
                .Select(row => row.PersonId)
                .ToListAsync(cancellationToken))
            .Distinct().ToArray();
        if (candidates.Length > 1) return null;
        long id;
        if (candidates.Length == 1)
        {
            id = candidates[0];
            var person = await db.Queryable<Person>()
                .Where(row => row.Id == id && row.UserId == run.UserId)
                .FirstAsync(cancellationToken);
            if (person is not null)
            {
                if (person.Relationship == "Unknown" && item.Relationship != "Unknown") person.Relationship = item.Relationship;
                person.RelationshipKeywords = person.RelationshipKeywords.Concat(item.RelationshipKeywords).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();
                person.CoverAttachmentId ??= attachmentIds.FirstOrDefault() is var cover && cover > 0 ? cover : null;
                person.UpdatedAt = now;
                await db.Updateable(person).ExecuteCommandAsync(cancellationToken);
            }
        }
        else
        {
            var person = new Person
            {
                UserId = run.UserId,
                Name = item.Name.Trim(),
                NormalizedName = normalized,
                Relationship = item.Relationship,
                RelationshipKeywords = item.RelationshipKeywords.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray(),
                CoverAttachmentId = attachmentIds.FirstOrDefault() is var cover && cover > 0 ? cover : null,
                CreatedAt = now,
                UpdatedAt = now,
            };
            id = await db.Insertable(person).ExecuteReturnBigIdentityAsync(cancellationToken);
        }
        foreach (var alias in item.Aliases.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var aliasNormalized = Normalize(alias);
            var exists = await db.Queryable<PersonAlias>()
                .AnyAsync(row => row.UserId == run.UserId
                    && row.PersonId == id
                    && row.NormalizedName == aliasNormalized, cancellationToken);
            if (!exists)
                await db.Insertable(new PersonAlias
                {
                    UserId = run.UserId,
                    PersonId = id,
                    Name = alias.Trim(),
                    NormalizedName = aliasNormalized,
                    SourceMessageId = run.TargetMessageId,
                    SourceMomentId = run.MomentId,
                    CreatedAt = now,
                }).ExecuteCommandAsync(cancellationToken);
        }
        return id;
    }

    /// <summary>按名称和已确认别名解析地点；无匹配时创建主卡。</summary>
    private async Task<long?> ResolvePlaceAsync(
        LifeRecordSubagent.PlaceItem item,
        AnalysisRun run,
        long[] attachmentIds,
        double? latitude,
        double? longitude,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(item.Name);
        var candidateIds = (await db.Queryable<Place>()
                .Where(row => row.UserId == run.UserId && row.NormalizedName == normalized)
                .Select(row => row.Id)
                .ToListAsync(cancellationToken))
            .Concat(await db.Queryable<PlaceAlias>()
                .Where(row => row.UserId == run.UserId && row.NormalizedName == normalized)
                .Select(row => row.PlaceId)
                .ToListAsync(cancellationToken))
            .Distinct().ToArray();
        var candidateRows = candidateIds.Length == 0
            ? []
            : await db.Queryable<Place>()
                .Where(row => row.UserId == run.UserId && candidateIds.Contains(row.Id))
                .ToListAsync(cancellationToken);
        var candidates = candidateRows.ToArray();
        if (latitude.HasValue && longitude.HasValue && candidates.Length > 0)
        {
            // ponytail: 1.5 km 足够 MVP 区分同名地点；地图检索上线时改为按地点类型配置或 PostGIS 半径。
            var nearby = candidates
                .Where(row => row.Latitude.HasValue
                    && row.Longitude.HasValue
                    && DistanceMeters(latitude.Value, longitude.Value, row.Latitude.Value, row.Longitude.Value) <= 1_500)
                .ToArray();
            candidates = nearby.Length > 0
                ? nearby
                : candidates.Where(row => !row.Latitude.HasValue || !row.Longitude.HasValue).ToArray();
        }
        if (candidates.Length > 1) return null;
        long id;
        if (candidates.Length == 1)
        {
            var place = candidates[0];
            id = place.Id;
            place.Province ??= item.Province;
            place.City ??= item.City;
            place.Latitude ??= latitude;
            place.Longitude ??= longitude;
            place.CoverAttachmentId ??= attachmentIds.FirstOrDefault() is var cover && cover > 0 ? cover : null;
            place.UpdatedAt = now;
            await db.Updateable(place).ExecuteCommandAsync(cancellationToken);
        }
        else
        {
            var place = new Place
            {
                UserId = run.UserId,
                Name = item.Name.Trim(),
                NormalizedName = normalized,
                Province = item.Province,
                City = item.City,
                Latitude = latitude,
                Longitude = longitude,
                CoverAttachmentId = attachmentIds.FirstOrDefault() is var cover && cover > 0 ? cover : null,
                CreatedAt = now,
                UpdatedAt = now,
            };
            id = await db.Insertable(place).ExecuteReturnBigIdentityAsync(cancellationToken);
        }
        foreach (var alias in item.Aliases.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var aliasNormalized = Normalize(alias);
            var exists = await db.Queryable<PlaceAlias>()
                .AnyAsync(row => row.UserId == run.UserId
                    && row.PlaceId == id
                    && row.NormalizedName == aliasNormalized, cancellationToken);
            if (!exists)
                await db.Insertable(new PlaceAlias
                {
                    UserId = run.UserId,
                    PlaceId = id,
                    Name = alias.Trim(),
                    NormalizedName = aliasNormalized,
                    SourceMessageId = run.TargetMessageId,
                    SourceMomentId = run.MomentId,
                    CreatedAt = now,
                }).ExecuteCommandAsync(cancellationToken);
        }
        return id;
    }

    /// <summary>按图片原始顺序读取第一组可靠 EXIF 坐标；每张图片的原始坐标仍完整保存在附件表。</summary>
    private static (double? Latitude, double? Longitude) FirstCoordinate(
        IEnumerable<long> attachmentIds,
        IReadOnlyDictionary<long, Attachment> attachments)
    {
        foreach (var id in attachmentIds)
            if (attachments.TryGetValue(id, out var attachment)
                && attachment.Latitude.HasValue
                && attachment.Longitude.HasValue)
                return (attachment.Latitude, attachment.Longitude);
        return (null, null);
    }

    /// <summary>计算两组 WGS84 坐标之间的球面距离，单位米。</summary>
    private static double DistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthRadius = 6_371_000;
        var lat1 = latitude1 * Math.PI / 180;
        var lat2 = latitude2 * Math.PI / 180;
        var deltaLat = (latitude2 - latitude1) * Math.PI / 180;
        var deltaLon = (longitude2 - longitude1) * Math.PI / 180;
        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
            + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
        return earthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>解析事件中提到的人物名称。</summary>
    private async Task<long[]> ResolvePersonNamesAsync(
        IEnumerable<string> names,
        IReadOnlyDictionary<string, long> current,
        long userId,
        CancellationToken cancellationToken)
    {
        var ids = new List<long>();
        foreach (var name in names)
        {
            if (current.TryGetValue(name, out var currentId)) { ids.Add(currentId); continue; }
            var normalized = Normalize(name);
            var matches = (await db.Queryable<Person>()
                    .Where(row => row.UserId == userId && row.NormalizedName == normalized)
                    .Select(row => row.Id)
                    .ToListAsync(cancellationToken))
                .Concat(await db.Queryable<PersonAlias>()
                    .Where(row => row.UserId == userId && row.NormalizedName == normalized)
                    .Select(row => row.PersonId)
                    .ToListAsync(cancellationToken))
                .Distinct().ToArray();
            if (matches.Length == 1) ids.Add(matches[0]);
        }
        return ids.Distinct().ToArray();
    }

    /// <summary>解析事件中提到的地点名称。</summary>
    private async Task<long[]> ResolvePlaceNamesAsync(
        IEnumerable<string> names,
        IReadOnlyDictionary<string, long> current,
        long userId,
        CancellationToken cancellationToken)
    {
        var ids = new List<long>();
        foreach (var name in names)
        {
            if (current.TryGetValue(name, out var currentId)) { ids.Add(currentId); continue; }
            var normalized = Normalize(name);
            var matches = (await db.Queryable<Place>()
                    .Where(row => row.UserId == userId && row.NormalizedName == normalized)
                    .Select(row => row.Id)
                    .ToListAsync(cancellationToken))
                .Concat(await db.Queryable<PlaceAlias>()
                    .Where(row => row.UserId == userId && row.NormalizedName == normalized)
                    .Select(row => row.PlaceId)
                    .ToListAsync(cancellationToken))
                .Distinct().ToArray();
            if (matches.Length == 1) ids.Add(matches[0]);
        }
        return ids.Distinct().ToArray();
    }

    /// <summary>保存一个需要用户稍后处理的人物或地点。</summary>
    private Task<int> InsertUnresolvedAsync(
        AnalysisRun run,
        string kind,
        string mention,
        string reason,
        long[] attachmentIds,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.Insertable(new UnresolvedMention
        {
            UserId = run.UserId,
            ConversationId = run.ConversationId,
            Kind = kind,
            Mention = mention,
            Reason = reason,
            SourceMessageId = run.TargetMessageId,
            SourceMomentId = run.MomentId,
            AttachmentIds = attachmentIds,
            Status = "Pending",
            CreatedAt = now,
        }).ExecuteCommandAsync(cancellationToken);

    /// <summary>标准化人物和地点名称。</summary>
    private static string Normalize(string? value) =>
        string.Concat((value ?? string.Empty).Where(character => !char.IsWhiteSpace(character))).ToLowerInvariant();
}
