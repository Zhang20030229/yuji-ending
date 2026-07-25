using Echora.Api.Authentication;
using Echora.Api.Entities;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>为拾光页面组合片段、人物、地点、事件和待确认数据。</summary>
public sealed class ArchiveViewService(ISqlSugarClient db, IHttpContextAccessor accessor)
{
    private long UserId => accessor.HttpContext?.User.GetRequiredId()
        ?? throw new UnauthorizedAccessException("当前请求没有用户身份。");

    /// <summary>读取已经生成总结的会话片段。</summary>
    public async Task<IReadOnlyList<FragmentItem>> GetFragmentsAsync(string? query, CancellationToken cancellationToken)
    {
        var conversations = await db.Queryable<Conversation>()
            .Where(item => item.UserId == UserId && item.Summary != null)
            .OrderBy(item => item.LastMessageAt, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(query))
            conversations = conversations.Where(item => Contains(item.Title, query) || Contains(item.Summary, query)).ToList();
        var ids = conversations.Select(item => item.Id).ToArray();
        var people = ids.Length == 0 ? [] : await db.Queryable<PersonRecord>().Where(item => item.UserId == UserId && item.ConversationId != null && ids.Contains(item.ConversationId.Value)).ToListAsync(cancellationToken);
        var places = ids.Length == 0 ? [] : await db.Queryable<PlaceRecord>().Where(item => item.UserId == UserId && item.ConversationId != null && ids.Contains(item.ConversationId.Value)).ToListAsync(cancellationToken);
        var events = ids.Length == 0 ? [] : await db.Queryable<LifeEvent>().Where(item => item.UserId == UserId && item.ConversationId != null && ids.Contains(item.ConversationId.Value)).ToListAsync(cancellationToken);
        var messages = ids.Length == 0 ? [] : await db.Queryable<ConversationMessage>()
            .Where(item => item.UserId == UserId && ids.Contains(item.ConversationId) && item.Role == "User")
            .ToListAsync(cancellationToken);
        var messageIds = messages.Select(item => item.Id).ToArray();
        var attachments = messageIds.Length == 0
            ? []
            : await db.Queryable<Attachment>()
                .Where(item => item.UserId == UserId
                    && item.MessageId != null
                    && messageIds.Contains(item.MessageId.Value))
                .ToListAsync(cancellationToken);
        return conversations.Select(item => new FragmentItem(
            item.Id,
            item.Title,
            item.Summary ?? string.Empty,
            item.LastMessageAt ?? item.CreatedAt,
            messages.Count(row => row.ConversationId == item.Id && row.Sequence <= item.SummaryThroughSequence),
            people.Count(row => row.ConversationId == item.Id),
            places.Count(row => row.ConversationId == item.Id),
            events.Count(row => row.ConversationId == item.Id),
            CoverUrl(messages
                .Where(row => row.ConversationId == item.Id && row.Sequence <= item.SummaryThroughSequence)
                .OrderByDescending(row => row.Sequence)
                .SelectMany(row => attachments.Where(asset => asset.MessageId == row.Id).OrderByDescending(asset => asset.Id))
                .Select(asset => (long?)asset.Id)
                .FirstOrDefault()))).ToArray();
    }

    /// <summary>读取一个片段的原话、人物、地点和事件。</summary>
    public async Task<FragmentDetail?> GetFragmentAsync(long id, CancellationToken cancellationToken)
    {
        var conversation = await db.Queryable<Conversation>().Where(item => item.Id == id && item.UserId == UserId).FirstAsync(cancellationToken);
        if (conversation?.Summary is null) return null;
        var messages = await db.Queryable<ConversationMessage>()
            .Where(item => item.UserId == UserId && item.ConversationId == id && item.Role == "User")
            .OrderBy(item => item.Sequence)
            .ToListAsync(cancellationToken);
        var personRecords = await db.Queryable<PersonRecord>().Where(item => item.UserId == UserId && item.ConversationId == id).ToListAsync(cancellationToken);
        var placeRecords = await db.Queryable<PlaceRecord>().Where(item => item.UserId == UserId && item.ConversationId == id).ToListAsync(cancellationToken);
        var events = await db.Queryable<LifeEvent>().Where(item => item.UserId == UserId && item.ConversationId == id).ToListAsync(cancellationToken);
        var people = await LoadPeopleAsync(personRecords.Select(item => item.PersonId), cancellationToken);
        var places = await LoadPlacesAsync(placeRecords.Select(item => item.PlaceId), cancellationToken);
        return new FragmentDetail(
            id,
            conversation.Title,
            conversation.Summary,
            conversation.LastMessageAt ?? conversation.CreatedAt,
            await ToSourcesAsync(messages, cancellationToken),
            people.Select(item => new EntityLink(item.Key, item.Value)).ToArray(),
            places.Select(item => new EntityLink(item.Key, item.Value)).ToArray(),
            events.Select(item => new NamedSummary(item.Id, item.Title, item.Summary)).ToArray());
    }

    /// <summary>读取人物卡片。</summary>
    public async Task<IReadOnlyList<EntityCard>> GetPeopleAsync(string? query, CancellationToken cancellationToken)
    {
        var people = await db.Queryable<Person>().Where(item => item.UserId == UserId).OrderBy(item => item.UpdatedAt, OrderByType.Desc).ToListAsync(cancellationToken);
        var aliases = await db.Queryable<PersonAlias>().Where(item => item.UserId == UserId).ToListAsync(cancellationToken);
        var records = await db.Queryable<PersonRecord>().Where(item => item.UserId == UserId).ToListAsync(cancellationToken);
        var events = await db.Queryable<LifeEvent>().Where(item => item.UserId == UserId).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(query))
            people = people.Where(item => Contains(item.Name, query)
                || Contains(item.Relationship, query)
                || item.RelationshipKeywords.Any(value => Contains(value, query))
                || aliases.Any(alias => alias.PersonId == item.Id && Contains(alias.Name, query))
                || records.Any(record => record.PersonId == item.Id && Contains(record.Summary, query))).ToList();
        return people.Select(item =>
        {
            var personRecords = records.Where(row => row.PersonId == item.Id).OrderByDescending(row => row.CreatedAt).ToArray();
            return new EntityCard(
                item.Id,
                item.Name,
                item.Relationship,
                item.RelationshipKeywords,
                aliases.Where(alias => alias.PersonId == item.Id).Select(alias => alias.Name).ToArray(),
                personRecords.FirstOrDefault()?.Summary ?? string.Empty,
                personRecords.Select(row => row.ConversationId).Where(id => id.HasValue).Distinct().Count(),
                events.Count(lifeEvent => lifeEvent.PersonIds.Contains(item.Id)),
                CoverUrl(item.CoverAttachmentId),
                null,
                null);
        }).ToArray();
    }

    /// <summary>读取人物详情。</summary>
    public async Task<EntityDetail?> GetPersonAsync(long id, CancellationToken cancellationToken)
    {
        var person = await db.Queryable<Person>().Where(item => item.Id == id && item.UserId == UserId).FirstAsync(cancellationToken);
        if (person is null) return null;
        var aliases = await db.Queryable<PersonAlias>().Where(item => item.UserId == UserId && item.PersonId == id).ToListAsync(cancellationToken);
        var records = await db.Queryable<PersonRecord>().Where(item => item.UserId == UserId && item.PersonId == id).OrderBy(item => item.CreatedAt, OrderByType.Desc).ToListAsync(cancellationToken);
        var events = await db.Queryable<LifeEvent>().Where(item => item.UserId == UserId).ToListAsync(cancellationToken);
        return new EntityDetail(
            id,
            person.Name,
            person.Relationship,
            person.RelationshipKeywords,
            aliases.Select(item => item.Name).ToArray(),
            CoverUrl(person.CoverAttachmentId),
            await LoadImagesAsync(records.SelectMany(item => item.AttachmentIds), cancellationToken),
            await ToEntityRecordsAsync(records.Select(item => new SourceRecord(item.Id, item.ConversationId, item.SourceMessageId, item.SourceMomentId, item.Summary)).ToArray(), cancellationToken),
            events.Where(item => item.PersonIds.Contains(id)).Select(item => new NamedSummary(item.Id, item.Title, item.Summary)).ToArray(),
            null,
            null);
    }

    /// <summary>读取地点卡片。</summary>
    public async Task<IReadOnlyList<EntityCard>> GetPlacesAsync(string? query, CancellationToken cancellationToken)
    {
        var places = await db.Queryable<Place>().Where(item => item.UserId == UserId).OrderBy(item => item.UpdatedAt, OrderByType.Desc).ToListAsync(cancellationToken);
        var aliases = await db.Queryable<PlaceAlias>().Where(item => item.UserId == UserId).ToListAsync(cancellationToken);
        var records = await db.Queryable<PlaceRecord>().Where(item => item.UserId == UserId).ToListAsync(cancellationToken);
        var events = await db.Queryable<LifeEvent>().Where(item => item.UserId == UserId).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(query))
            places = places.Where(item => Contains(item.Name, query) || Contains(item.Province, query) || Contains(item.City, query)
                || aliases.Any(alias => alias.PlaceId == item.Id && Contains(alias.Name, query))
                || records.Any(record => record.PlaceId == item.Id && Contains(record.Summary, query))).ToList();
        return places.Select(item =>
        {
            var placeRecords = records.Where(row => row.PlaceId == item.Id).OrderByDescending(row => row.CreatedAt).ToArray();
            return new EntityCard(
                item.Id,
                item.Name,
                string.Join(' ', new[] { item.Province, item.City }.Where(value => !string.IsNullOrWhiteSpace(value))),
                [],
                aliases.Where(alias => alias.PlaceId == item.Id).Select(alias => alias.Name).ToArray(),
                placeRecords.FirstOrDefault()?.Summary ?? string.Empty,
                placeRecords.Select(row => row.ConversationId).Where(id => id.HasValue).Distinct().Count(),
                events.Count(lifeEvent => lifeEvent.PlaceIds.Contains(item.Id)),
                CoverUrl(item.CoverAttachmentId),
                item.Latitude,
                item.Longitude);
        }).ToArray();
    }

    /// <summary>读取地点详情。</summary>
    public async Task<EntityDetail?> GetPlaceAsync(long id, CancellationToken cancellationToken)
    {
        var place = await db.Queryable<Place>().Where(item => item.Id == id && item.UserId == UserId).FirstAsync(cancellationToken);
        if (place is null) return null;
        var aliases = await db.Queryable<PlaceAlias>().Where(item => item.UserId == UserId && item.PlaceId == id).ToListAsync(cancellationToken);
        var records = await db.Queryable<PlaceRecord>().Where(item => item.UserId == UserId && item.PlaceId == id).OrderBy(item => item.CreatedAt, OrderByType.Desc).ToListAsync(cancellationToken);
        var events = await db.Queryable<LifeEvent>().Where(item => item.UserId == UserId).ToListAsync(cancellationToken);
        return new EntityDetail(
            id,
            place.Name,
            string.Join(' ', new[] { place.Province, place.City }.Where(value => !string.IsNullOrWhiteSpace(value))),
            [],
            aliases.Select(item => item.Name).ToArray(),
            CoverUrl(place.CoverAttachmentId),
            await LoadImagesAsync(records.SelectMany(item => item.AttachmentIds), cancellationToken),
            await ToEntityRecordsAsync(records.Select(item => new SourceRecord(item.Id, item.ConversationId, item.SourceMessageId, item.SourceMomentId, item.Summary)).ToArray(), cancellationToken),
            events.Where(item => item.PlaceIds.Contains(id)).Select(item => new NamedSummary(item.Id, item.Title, item.Summary)).ToArray(),
            place.Latitude,
            place.Longitude);
    }

    /// <summary>读取事件卡片。</summary>
    public async Task<IReadOnlyList<EventItem>> GetEventsAsync(string? query, CancellationToken cancellationToken)
    {
        var events = await db.Queryable<LifeEvent>().Where(item => item.UserId == UserId).OrderBy(item => item.OccurredAt, OrderByType.Desc).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(query)) events = events.Where(item => Contains(item.Title, query) || Contains(item.Summary, query)).ToList();
        var people = await LoadPeopleAsync(events.SelectMany(item => item.PersonIds), cancellationToken);
        var placeRows = await LoadPlaceRowsAsync(events.SelectMany(item => item.PlaceIds), cancellationToken);
        var places = placeRows.ToDictionary(item => item.Id, item => item.Name);
        var placeRowsById = placeRows.ToDictionary(item => item.Id);
        return events.Select(item => new EventItem(
            item.Id,
            item.Title,
            item.Summary,
            item.OccurredAt,
            item.PersonIds.Where(people.ContainsKey).Select(id => people[id]).ToArray(),
            item.PlaceIds.Where(places.ContainsKey).Select(id => places[id]).ToArray(),
            item.PlaceIds
                .Where(placeRowsById.ContainsKey)
                .Select(placeId => placeRowsById[placeId])
                .Select(place => new EventLocation(
                    place.Id,
                    place.Name,
                    string.Join(' ', new[] { place.Province, place.City }.Where(value => !string.IsNullOrWhiteSpace(value))),
                    place.Latitude,
                    place.Longitude))
                .ToArray(),
            CoverUrl(item.AttachmentIds.FirstOrDefault() is var cover && cover > 0 ? cover : null))).ToArray();
    }

    /// <summary>读取事件详情。</summary>
    public async Task<EventDetail?> GetEventAsync(long id, CancellationToken cancellationToken)
    {
        var item = await db.Queryable<LifeEvent>().Where(row => row.Id == id && row.UserId == UserId).FirstAsync(cancellationToken);
        if (item is null) return null;
        var people = await LoadPeopleAsync(item.PersonIds, cancellationToken);
        var placeRows = await LoadPlaceRowsAsync(item.PlaceIds, cancellationToken);
        var places = placeRows.ToDictionary(place => place.Id, place => place.Name);
        var locations = placeRows
            .Select(place => new EventLocation(
                place.Id,
                place.Name,
                string.Join(' ', new[] { place.Province, place.City }.Where(value => !string.IsNullOrWhiteSpace(value))),
                place.Latitude,
                place.Longitude))
            .ToArray();
        return new EventDetail(
            item.Id,
            item.Title,
            item.Summary,
            item.OccurredAt,
            item.ConversationId ?? 0,
            item.PersonIds.Where(people.ContainsKey).Select(personId => people[personId]).ToArray(),
            item.PlaceIds.Where(places.ContainsKey).Select(placeId => places[placeId]).ToArray(),
            locations,
            await LoadSourceAsync(item.SourceMessageId, item.SourceMomentId, cancellationToken),
            await LoadImagesAsync(item.AttachmentIds, cancellationToken));
    }

    /// <summary>读取待确认人物和地点。</summary>
    public async Task<IReadOnlyList<PendingItem>> GetPendingAsync(CancellationToken cancellationToken)
    {
        var pending = await db.Queryable<UnresolvedMention>().Where(item => item.UserId == UserId && item.Status == "Pending").OrderBy(item => item.CreatedAt, OrderByType.Desc).ToListAsync(cancellationToken);
        var conversationIds = pending.Where(item => item.ConversationId.HasValue).Select(item => item.ConversationId!.Value).Distinct().ToArray();
        var conversations = conversationIds.Length == 0 ? [] : await db.Queryable<Conversation>().Where(item => item.UserId == UserId && conversationIds.Contains(item.Id)).ToListAsync(cancellationToken);
        var conversationTitles = conversations.ToDictionary(item => item.Id, item => item.Title);
        var sources = await LoadSourcesAsync(
            pending.Select(item => (item.SourceMessageId, item.SourceMomentId)),
            cancellationToken);
        return pending.Select(item => new PendingItem(
                item.Id,
                item.Kind,
                item.Mention,
                item.Reason,
                item.ConversationId ?? 0,
                item.ConversationId.HasValue
                    && conversationTitles.TryGetValue(item.ConversationId.Value, out var title)
                    ? title
                    : "一刻",
                item.CreatedAt,
                GetSources(sources, item.SourceMessageId, item.SourceMomentId)))
            .ToArray();
    }

    /// <summary>保存用户对一条人物记录 AI 总结的人工修订。</summary>
    public async Task<bool> UpdatePersonRecordSummaryAsync(long id, string summary, CancellationToken cancellationToken)
    {
        var item = await db.Queryable<PersonRecord>()
            .Where(row => row.Id == id && row.UserId == UserId)
            .FirstAsync(cancellationToken);
        if (item is null) return false;
        item.Summary = ValidSummary(summary);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        return await db.Updateable(item).ExecuteCommandAsync(cancellationToken) > 0;
    }

    /// <summary>保存用户对一条地点记录 AI 总结的人工修订。</summary>
    public async Task<bool> UpdatePlaceRecordSummaryAsync(long id, string summary, CancellationToken cancellationToken)
    {
        var item = await db.Queryable<PlaceRecord>()
            .Where(row => row.Id == id && row.UserId == UserId)
            .FirstAsync(cancellationToken);
        if (item is null) return false;
        item.Summary = ValidSummary(summary);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        return await db.Updateable(item).ExecuteCommandAsync(cancellationToken) > 0;
    }

    /// <summary>保存用户对一条事件 AI 总结的人工修订。</summary>
    public async Task<bool> UpdateEventSummaryAsync(long id, string summary, CancellationToken cancellationToken)
    {
        var item = await db.Queryable<LifeEvent>()
            .Where(row => row.Id == id && row.UserId == UserId)
            .FirstAsync(cancellationToken);
        if (item is null) return false;
        item.Summary = ValidSummary(summary);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        return await db.Updateable(item).ExecuteCommandAsync(cancellationToken) > 0;
    }

    /// <summary>只删除用户选中的事件，不删除原聊天、人物、地点或图片。</summary>
    public Task<int> DeleteEventAsync(long id, CancellationToken cancellationToken) =>
        db.Deleteable<LifeEvent>()
            .Where(item => item.Id == id && item.UserId == UserId)
            .ExecuteCommandAsync(cancellationToken);

    /// <summary>把待确认项绑定到已有对象、创建新对象或忽略。</summary>
    public async Task<bool> ResolvePendingAsync(long id, long? entityId, string? newName, bool ignore, CancellationToken cancellationToken)
    {
        var item = await db.Queryable<UnresolvedMention>().Where(row => row.Id == id && row.UserId == UserId).FirstAsync(cancellationToken);
        if (item is null || item.Status != "Pending") return false;
        if (!ignore && entityId is null && string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("请选择已有对象或填写新名称。");
        var sourceAttachments = item.AttachmentIds.Length == 0
            ? []
            : await db.Queryable<Attachment>()
                .Where(row => row.UserId == UserId && item.AttachmentIds.Contains(row.Id))
                .ToListAsync(cancellationToken);
        var coverAttachmentId = item.AttachmentIds.FirstOrDefault(id => sourceAttachments.Any(asset => asset.Id == id));
        var coordinate = sourceAttachments.FirstOrDefault(asset => asset.Latitude.HasValue && asset.Longitude.HasValue);
        long? resolvedId = entityId;
        db.Ado.BeginTran();
        try
        {
            if (!ignore && resolvedId is null)
            {
                if (item.Kind == "Person")
                    resolvedId = await db.Insertable(new Person
                    {
                        UserId = UserId,
                        Name = newName!.Trim(),
                        NormalizedName = Normalize(newName),
                        CoverAttachmentId = coverAttachmentId > 0 ? coverAttachmentId : null,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    }).ExecuteReturnBigIdentityAsync(cancellationToken);
                else
                    resolvedId = await db.Insertable(new Place
                    {
                        UserId = UserId,
                        Name = newName!.Trim(),
                        NormalizedName = Normalize(newName),
                        Latitude = coordinate?.Latitude,
                        Longitude = coordinate?.Longitude,
                        CoverAttachmentId = coverAttachmentId > 0 ? coverAttachmentId : null,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    }).ExecuteReturnBigIdentityAsync(cancellationToken);
            }
            if (!ignore && resolvedId.HasValue)
            {
                var exists = item.Kind == "Person"
                    ? await db.Queryable<Person>().AnyAsync(row => row.Id == resolvedId.Value && row.UserId == UserId, cancellationToken)
                    : await db.Queryable<Place>().AnyAsync(row => row.Id == resolvedId.Value && row.UserId == UserId, cancellationToken);
                if (!exists) throw new ArgumentException("选择的对象不存在。");
                if (item.Kind == "Person")
                {
                    var person = await db.Queryable<Person>()
                        .Where(row => row.Id == resolvedId.Value && row.UserId == UserId)
                        .FirstAsync(cancellationToken);
                    if (person is not null && person.CoverAttachmentId is null && coverAttachmentId > 0)
                    {
                        person.CoverAttachmentId = coverAttachmentId;
                        person.UpdatedAt = DateTimeOffset.UtcNow;
                        await db.Updateable(person).ExecuteCommandAsync(cancellationToken);
                    }
                    await db.Insertable(new PersonRecord { UserId = UserId, PersonId = resolvedId.Value, ConversationId = item.ConversationId, Summary = item.Reason, SourceMessageId = item.SourceMessageId, SourceMomentId = item.SourceMomentId, AttachmentIds = item.AttachmentIds }).ExecuteCommandAsync(cancellationToken);
                }
                else
                {
                    var place = await db.Queryable<Place>()
                        .Where(row => row.Id == resolvedId.Value && row.UserId == UserId)
                        .FirstAsync(cancellationToken);
                    if (place is not null)
                    {
                        place.CoverAttachmentId ??= coverAttachmentId > 0 ? coverAttachmentId : null;
                        place.Latitude ??= coordinate?.Latitude;
                        place.Longitude ??= coordinate?.Longitude;
                        place.UpdatedAt = DateTimeOffset.UtcNow;
                        await db.Updateable(place).ExecuteCommandAsync(cancellationToken);
                    }
                    await db.Insertable(new PlaceRecord { UserId = UserId, PlaceId = resolvedId.Value, ConversationId = item.ConversationId, Summary = item.Reason, SourceMessageId = item.SourceMessageId, SourceMomentId = item.SourceMomentId, AttachmentIds = item.AttachmentIds }).ExecuteCommandAsync(cancellationToken);
                }
            }
            item.Status = ignore ? "Ignored" : "Resolved";
            item.ResolvedEntityId = ignore ? null : resolvedId;
            item.ResolvedAt = DateTimeOffset.UtcNow;
            await db.Updateable(item).ExecuteCommandAsync(cancellationToken);
            db.Ado.CommitTran();
            return true;
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    /// <summary>加载人物名称字典。</summary>
    private async Task<Dictionary<long, string>> LoadPeopleAsync(IEnumerable<long> ids, CancellationToken cancellationToken)
    {
        var values = ids.Distinct().ToArray();
        if (values.Length == 0) return [];
        return (await db.Queryable<Person>().Where(item => item.UserId == UserId && values.Contains(item.Id)).ToListAsync(cancellationToken)).ToDictionary(item => item.Id, item => item.Name);
    }

    /// <summary>加载地点名称字典。</summary>
    private async Task<Dictionary<long, string>> LoadPlacesAsync(IEnumerable<long> ids, CancellationToken cancellationToken)
    {
        var values = ids.Distinct().ToArray();
        if (values.Length == 0) return [];
        return (await db.Queryable<Place>().Where(item => item.UserId == UserId && values.Contains(item.Id)).ToListAsync(cancellationToken)).ToDictionary(item => item.Id, item => item.Name);
    }

    /// <summary>把来源记录转换成详情页条目。</summary>
    private async Task<IReadOnlyList<EntityRecord>> ToEntityRecordsAsync(IReadOnlyList<SourceRecord> records, CancellationToken cancellationToken)
    {
        var conversationIds = records.Where(item => item.ConversationId.HasValue).Select(item => item.ConversationId!.Value).Distinct().ToArray();
        var conversations = conversationIds.Length == 0 ? [] : await db.Queryable<Conversation>().Where(item => item.UserId == UserId && conversationIds.Contains(item.Id)).ToListAsync(cancellationToken);
        var conversationTitles = conversations.ToDictionary(item => item.Id, item => item.Title);
        var sources = await LoadSourcesAsync(
            records.Select(item => (item.SourceMessageId, item.SourceMomentId)),
            cancellationToken);
        return records.Select(item => new EntityRecord(
                item.Id,
                item.ConversationId ?? 0,
                item.ConversationId.HasValue
                    && conversationTitles.TryGetValue(item.ConversationId.Value, out var title)
                    ? title
                    : "一刻",
                item.Summary,
                GetSources(sources, item.SourceMessageId, item.SourceMomentId)))
            .ToArray();
    }

    /// <summary>加载地点及其坐标，供事件列表和地图一次使用。</summary>
    private async Task<List<Place>> LoadPlaceRowsAsync(IEnumerable<long> ids, CancellationToken cancellationToken)
    {
        var values = ids.Distinct().ToArray();
        return values.Length == 0
            ? []
            : await db.Queryable<Place>()
                .Where(item => item.UserId == UserId && values.Contains(item.Id))
                .ToListAsync(cancellationToken);
    }

    /// <summary>加载一个聊天消息或一刻的原话。</summary>
    private async Task<IReadOnlyList<SourceMessage>> LoadSourceAsync(long? messageId, long? momentId, CancellationToken cancellationToken)
    {
        var sources = await LoadSourcesAsync([(messageId, momentId)], cancellationToken);
        return GetSources(sources, messageId, momentId);
    }

    /// <summary>一次性加载一批聊天或一刻来源，避免详情和待确认列表按条访问数据库。</summary>
    private async Task<Dictionary<(long? MessageId, long? MomentId), IReadOnlyList<SourceMessage>>> LoadSourcesAsync(
        IEnumerable<(long? MessageId, long? MomentId)> sourceKeys,
        CancellationToken cancellationToken)
    {
        var keys = sourceKeys.Distinct().ToArray();
        var messageIds = keys.Where(item => item.MessageId.HasValue).Select(item => item.MessageId!.Value).Distinct().ToArray();
        var momentIds = keys.Where(item => item.MomentId.HasValue).Select(item => item.MomentId!.Value).Distinct().ToArray();
        var messages = messageIds.Length == 0
            ? []
            : await db.Queryable<ConversationMessage>()
                .Where(item => item.UserId == UserId && messageIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
        var moments = momentIds.Length == 0
            ? []
            : await db.Queryable<Moment>()
                .Where(item => item.UserId == UserId && momentIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
        var messageSources = (await ToSourcesAsync(messages, cancellationToken))
            .ToDictionary(item => item.Id);
        var momentImages = (await LoadImagesAsync(moments.Select(item => item.AttachmentId), cancellationToken))
            .ToDictionary(item => item.Id);
        var momentById = moments.ToDictionary(item => item.Id);
        var result = new Dictionary<(long?, long?), IReadOnlyList<SourceMessage>>();
        foreach (var key in keys)
        {
            if (key.MessageId is long messageId && messageSources.TryGetValue(messageId, out var message))
                result[key] = [message];
            else if (key.MomentId is long momentId && momentById.TryGetValue(momentId, out var moment))
                result[key] =
                [
                    new SourceMessage(
                        moment.Id,
                        "Moment",
                        moment.Text ?? string.Empty,
                        moment.PublishedAt,
                        moment.LocationName,
                        moment.LocationAddress,
                        momentImages.TryGetValue(moment.AttachmentId, out var image) ? [image] : [])
                ];
            else
                result[key] = [];
        }
        return result;
    }

    /// <summary>读取批量来源字典中的一条记录。</summary>
    private static IReadOnlyList<SourceMessage> GetSources(
        IReadOnlyDictionary<(long? MessageId, long? MomentId), IReadOnlyList<SourceMessage>> sources,
        long? messageId,
        long? momentId) =>
        sources.TryGetValue((messageId, momentId), out var result) ? result : [];

    /// <summary>把消息转换为包含图片的原话引用。</summary>
    private async Task<IReadOnlyList<SourceMessage>> ToSourcesAsync(IReadOnlyList<ConversationMessage> messages, CancellationToken cancellationToken)
    {
        var ids = messages.Select(item => item.Id).ToArray();
        var attachments = ids.Length == 0 ? [] : await db.Queryable<Attachment>().Where(item => item.UserId == UserId && item.MessageId != null && ids.Contains(item.MessageId.Value)).ToListAsync(cancellationToken);
        return messages.Select(item => new SourceMessage(
            item.Id,
            "Message",
            item.Text ?? string.Empty,
            item.CreatedAt,
            item.LocationName,
            item.LocationAddress,
            attachments.Where(asset => asset.MessageId == item.Id).Select(ToAttachment).ToArray())).ToArray();
    }

    /// <summary>加载去重后的图片附件。</summary>
    private async Task<IReadOnlyList<AttachmentItem>> LoadImagesAsync(IEnumerable<long> ids, CancellationToken cancellationToken)
    {
        var values = ids.Where(id => id > 0).Distinct().ToArray();
        if (values.Length == 0) return [];
        return (await db.Queryable<Attachment>().Where(item => item.UserId == UserId && values.Contains(item.Id)).ToListAsync(cancellationToken)).Select(ToAttachment).ToArray();
    }

    private static AttachmentItem ToAttachment(Attachment item) => new(
        item.Id,
        item.OriginalFileName,
        AttachmentService.GetReadableMimeType(item),
        $"/api/assets/{item.Id}/content",
        item.AiDescription);
    private static string? CoverUrl(long? id) => id is > 0 ? $"/api/assets/{id}/thumbnail" : null;
    private static bool Contains(string? value, string query) => value?.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) == true;
    private static string Normalize(string? value) => string.Concat((value ?? string.Empty).Where(character => !char.IsWhiteSpace(character))).ToLowerInvariant();
    private static string ValidSummary(string? value)
    {
        var summary = value?.Trim() ?? string.Empty;
        if (summary.Length is < 1 or > 2_000) throw new ArgumentException("总结长度必须为 1 到 2000 个字符。");
        return summary;
    }

    public sealed record FragmentItem(long Id, string Title, string Summary, DateTimeOffset OccurredAt, int SourceCount, int PersonCount, int PlaceCount, int EventCount, string? CoverImageUrl);
    public sealed record FragmentDetail(long Id, string Title, string Summary, DateTimeOffset OccurredAt, IReadOnlyList<SourceMessage> Sources, IReadOnlyList<EntityLink> People, IReadOnlyList<EntityLink> Places, IReadOnlyList<NamedSummary> Events);
    public sealed record EntityCard(long Id, string Name, string Secondary, IReadOnlyList<string> Keywords, IReadOnlyList<string> Aliases, string LatestSummary, int ConversationCount, int EventCount, string? CoverImageUrl, double? Latitude, double? Longitude);
    public sealed record EntityDetail(long Id, string Name, string Secondary, IReadOnlyList<string> Keywords, IReadOnlyList<string> Aliases, string? CoverImageUrl, IReadOnlyList<AttachmentItem> Images, IReadOnlyList<EntityRecord> Records, IReadOnlyList<NamedSummary> Events, double? Latitude, double? Longitude);
    public sealed record EntityRecord(long Id, long ConversationId, string ConversationTitle, string Summary, IReadOnlyList<SourceMessage> Sources);
    public sealed record EventItem(long Id, string Title, string Summary, DateTimeOffset OccurredAt, IReadOnlyList<string> People, IReadOnlyList<string> Places, IReadOnlyList<EventLocation> Locations, string? CoverImageUrl);
    public sealed record EventDetail(long Id, string Title, string Summary, DateTimeOffset OccurredAt, long ConversationId, IReadOnlyList<string> People, IReadOnlyList<string> Places, IReadOnlyList<EventLocation> Locations, IReadOnlyList<SourceMessage> Sources, IReadOnlyList<AttachmentItem> Images);
    public sealed record PendingItem(long Id, string Kind, string Mention, string Reason, long ConversationId, string ConversationTitle, DateTimeOffset CreatedAt, IReadOnlyList<SourceMessage> Sources);
    public sealed record SourceMessage(long Id, string SourceType, string Text, DateTimeOffset CreatedAt, string? LocationName, string? LocationAddress, IReadOnlyList<AttachmentItem> Attachments);
    public sealed record AttachmentItem(long Id, string FileName, string MimeType, string ContentUrl, string? AiDescription);
    public sealed record NamedSummary(long Id, string Title, string Summary);
    public sealed record EntityLink(long Id, string Name);
    public sealed record EventLocation(long Id, string Name, string Region, double? Latitude, double? Longitude);
    private sealed record SourceRecord(long Id, long? ConversationId, long? SourceMessageId, long? SourceMomentId, string Summary);
}
