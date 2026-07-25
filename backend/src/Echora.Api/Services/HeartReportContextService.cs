using System.Text.Json;
using Echora.Api.Entities;
using Echora.Api.Workflows;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>从既有结构化记录构建四个报告分区的确定性指标、证据和模型输入。</summary>
public sealed class HeartReportContextService(
    ISqlSugarClient db,
    ILogger<HeartReportContextService> logger)
{
    /// <summary>为一个报告包一次性准备四个互相隔离的子智能体输入。</summary>
    public async Task<HeartReportInput?> BuildAsync(
        ReportPack pack,
        CancellationToken cancellationToken)
    {
        var user = await db.Queryable<UserAccount>()
            .Where(item => item.Id == pack.UserId)
            .FirstAsync(cancellationToken);
        if (user is null) return null;

        var (start, endExclusive) = ToUtcRange(pack.StartDate, pack.EndDate);
        var messages = await db.Queryable<ConversationMessage>()
            .Where(item => item.UserId == pack.UserId
                && item.Role == "User"
                && item.Status == "Completed"
                && item.CreatedAt >= start
                && item.CreatedAt < endExclusive)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        var moments = await db.Queryable<Moment>()
            .Where(item => item.UserId == pack.UserId
                && item.PublishedAt >= start
                && item.PublishedAt < endExclusive)
            .OrderBy(item => item.PublishedAt)
            .ToListAsync(cancellationToken);

        var messageIds = messages.Select(item => item.Id).ToHashSet();
        var momentIds = moments.Select(item => item.Id).ToHashSet();
        var conversationIds = messages.Select(item => item.ConversationId).Distinct().ToArray();
        var conversations = conversationIds.Length == 0
            ? []
            : await db.Queryable<Conversation>()
                .Where(item => item.UserId == pack.UserId && conversationIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
        var attachments = await db.Queryable<Attachment>()
            .Where(item => item.UserId == pack.UserId)
            .ToListAsync(cancellationToken);
        attachments = attachments
            .Where(item => item.MessageId.HasValue && messageIds.Contains(item.MessageId.Value)
                || item.MomentId.HasValue && momentIds.Contains(item.MomentId.Value))
            .ToList();

        var personRecords = (await db.Queryable<PersonRecord>()
                .Where(item => item.UserId == pack.UserId)
                .ToListAsync(cancellationToken))
            .Where(item => InRange(item.SourceMessageId, item.SourceMomentId, messageIds, momentIds))
            .ToArray();
        var placeRecords = (await db.Queryable<PlaceRecord>()
                .Where(item => item.UserId == pack.UserId)
                .ToListAsync(cancellationToken))
            .Where(item => InRange(item.SourceMessageId, item.SourceMomentId, messageIds, momentIds))
            .ToArray();
        var events = (await db.Queryable<LifeEvent>()
                .Where(item => item.UserId == pack.UserId)
                .ToListAsync(cancellationToken))
            .Where(item => InRange(item.SourceMessageId, item.SourceMomentId, messageIds, momentIds))
            .ToArray();
        var recognitions = (await db.Queryable<Recognition>()
                .Where(item => item.UserId == pack.UserId)
                .ToListAsync(cancellationToken))
            .Where(item => InRange(item.SourceMessageId, item.SourceMomentId, messageIds, momentIds))
            .ToArray();
        var emotions = (await db.Queryable<EmotionRecord>()
                .Where(item => item.UserId == pack.UserId)
                .ToListAsync(cancellationToken))
            .Where(item => InRange(item.SourceMessageId, item.SourceMomentId, messageIds, momentIds))
            .ToArray();
        var cbtObservations = (await db.Queryable<CbtObservation>()
                .Where(item => item.UserId == pack.UserId)
                .ToListAsync(cancellationToken))
            .Where(item => InRange(item.SourceMessageId, item.SourceMomentId, messageIds, momentIds))
            .ToArray();

        var people = (await db.Queryable<Person>()
                .Where(item => item.UserId == pack.UserId)
                .ToListAsync(cancellationToken))
            .ToDictionary(item => item.Id);
        var places = (await db.Queryable<Place>()
                .Where(item => item.UserId == pack.UserId)
                .ToListAsync(cancellationToken))
            .ToDictionary(item => item.Id);
        var sources = SourceLookup(messages, moments, attachments);
        var allSourceKeys = CollectSourceKeys(
            messages,
            conversations,
            personRecords,
            placeRecords,
            events,
            recognitions,
            emotions,
            cbtObservations);
        var refs = allSourceKeys
            .Where(sources.ContainsKey)
            .OrderBy(key => sources[key].OccurredAt)
            .ThenBy(key => key, StringComparer.Ordinal)
            .Select((key, index) => (key, reference: $"E{index + 1}"))
            .ToDictionary(item => item.key, item => item.reference);

        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
        var period = new { startDate = pack.StartDate, endDate = pack.EndDate };
        var life = BuildLifeSection(
            displayName, period, messages, conversations, placeRecords, events,
            people, places, attachments, sources, refs);
        var relationship = BuildRelationshipSection(
            displayName, period, personRecords, events, people, attachments, sources, refs);
        var recognition = BuildRecognitionSection(
            displayName, period, recognitions, sources, refs);
        var emotion = BuildEmotionSection(
            displayName, period, emotions, cbtObservations, sources, refs);
        var latestWellbeing = await LatestWellbeingAsync(pack.UserId, cancellationToken);

        logger.LogInformation(
            "Heart report context prepared: ReportPackId {ReportPackId}, MessageCount {MessageCount}, MomentCount {MomentCount}, LifeEvidenceCount {LifeEvidenceCount}, EmotionEvidenceCount {EmotionEvidenceCount}, RelationshipEvidenceCount {RelationshipEvidenceCount}, RecognitionEvidenceCount {RecognitionEvidenceCount}",
            pack.Id,
            messages.Count,
            moments.Count,
            life.AllowedEvidenceRefs.Length,
            emotion.AllowedEvidenceRefs.Length,
            relationship.AllowedEvidenceRefs.Length,
            recognition.AllowedEvidenceRefs.Length);
        return new HeartReportInput(
            pack.Id,
            pack.UserId,
            displayName,
            pack.PeriodType,
            pack.StartDate,
            pack.EndDate,
            [life, emotion, relationship, recognition],
            latestWellbeing);
    }

    /// <summary>构建生活回望输入和指标。</summary>
    private static ReportSectionInput BuildLifeSection(
        string displayName,
        object period,
        IReadOnlyList<ConversationMessage> messages,
        IReadOnlyList<Conversation> conversations,
        IReadOnlyList<PlaceRecord> placeRecords,
        IReadOnlyList<LifeEvent> events,
        IReadOnlyDictionary<long, Person> people,
        IReadOnlyDictionary<long, Place> places,
        IReadOnlyList<Attachment> attachments,
        IReadOnlyDictionary<string, SourceValue> sources,
        IReadOnlyDictionary<string, string> refs)
    {
        var summarizedConversationIds = conversations
            .Where(item => !string.IsNullOrWhiteSpace(item.Summary))
            .Select(item => item.Id)
            .ToHashSet();
        var fragmentMessages = messages
            .Where(item => summarizedConversationIds.Contains(item.ConversationId))
            .ToArray();
        var sourceKeys = fragmentMessages.Select(item => MessageKey(item.Id))
            .Concat(placeRecords.Select(SourceKey))
            .Concat(events.Select(SourceKey))
            .Where(key => key is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var attachmentIds = attachments
            .Where(item => item.MessageId.HasValue && sourceKeys.Contains(MessageKey(item.MessageId.Value))
                || item.MomentId.HasValue && sourceKeys.Contains(MomentKey(item.MomentId.Value)))
            .Select(item => item.Id)
            .ToHashSet();
        foreach (var id in placeRecords.SelectMany(item => item.AttachmentIds)
                     .Concat(events.SelectMany(item => item.AttachmentIds)))
            attachmentIds.Add(id);
        var evidence = Evidence(sourceKeys, attachmentIds, sources, refs, attachments);
        var fragments = conversations
            .Where(item => summarizedConversationIds.Contains(item.Id)
                && fragmentMessages.Any(message => message.ConversationId == item.Id))
            .Select(item =>
            {
                var scoped = fragmentMessages
                    .Where(message => message.ConversationId == item.Id)
                    .OrderBy(message => message.CreatedAt)
                    .ToArray();
                return new
                {
                    title = $"{LocalDate(scoped[0].CreatedAt):yyyy-MM-dd} 的对话",
                    summary = $"本日期范围内有 {scoped.Length} 条原话，具体内容见对应依据。",
                    evidenceRefs = scoped
                    .Select(message => refs.GetValueOrDefault(MessageKey(message.Id)))
                    .Where(reference => reference is not null)
                    .ToArray(),
                };
            })
            .ToArray();
        var eventItems = events.Select(item => new
        {
            item.Title,
            item.Summary,
            item.OccurredAt,
            people = item.PersonIds.Where(people.ContainsKey).Select(id => people[id].Name).ToArray(),
            places = item.PlaceIds.Where(places.ContainsKey).Select(id => places[id].Name).ToArray(),
            evidenceRef = Reference(item.SourceMessageId, item.SourceMomentId, refs),
        }).ToArray();
        var placeItems = placeRecords.Where(item => places.ContainsKey(item.PlaceId)).Select(item => new
        {
            name = places[item.PlaceId].Name,
            item.Summary,
            evidenceRef = Reference(item.SourceMessageId, item.SourceMomentId, refs),
        }).ToArray();
        var metrics = new
        {
            recordedDays = evidence.Select(item => LocalDate(item.OccurredAt)).Distinct().Count(),
            fragmentCount = fragments.Length,
            eventCount = eventItems.Length,
            placeCount = placeRecords.Select(item => item.PlaceId).Distinct().Count(),
            photoCount = evidence.SelectMany(item => item.AttachmentIds).Distinct().Count(),
        };
        return Section(
            "Life",
            evidence.Length >= 2,
            metrics,
            new { userDisplayName = displayName, period, lifeRecords = new { fragments, events = eventItems, places = placeItems } },
            evidence);
    }

    /// <summary>构建人际往来输入和指标；只使用人物记录确认的图片。</summary>
    private static ReportSectionInput BuildRelationshipSection(
        string displayName,
        object period,
        IReadOnlyList<PersonRecord> personRecords,
        IReadOnlyList<LifeEvent> events,
        IReadOnlyDictionary<long, Person> people,
        IReadOnlyList<Attachment> attachments,
        IReadOnlyDictionary<string, SourceValue> sources,
        IReadOnlyDictionary<string, string> refs)
    {
        var relatedEvents = events.Where(item => item.PersonIds.Length > 0).ToArray();
        var sourceKeys = personRecords.Select(SourceKey)
            .Concat(relatedEvents.Select(SourceKey))
            .Where(key => key is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var personAttachmentIds = personRecords.SelectMany(item => item.AttachmentIds).ToHashSet();
        var evidence = Evidence(sourceKeys, personAttachmentIds, sources, refs, attachments);
        var records = personRecords.Where(item => people.ContainsKey(item.PersonId)).Select(item => new
        {
            name = people[item.PersonId].Name,
            relationship = people[item.PersonId].Relationship,
            relationshipKeywords = people[item.PersonId].RelationshipKeywords,
            item.Summary,
            evidenceRef = Reference(item.SourceMessageId, item.SourceMomentId, refs),
        }).ToArray();
        var eventItems = relatedEvents.Select(item => new
        {
            item.Title,
            item.Summary,
            people = item.PersonIds.Where(people.ContainsKey).Select(id => people[id].Name).ToArray(),
            evidenceRef = Reference(item.SourceMessageId, item.SourceMomentId, refs),
        }).ToArray();
        var metrics = new
        {
            peopleCount = personRecords.Select(item => item.PersonId).Distinct().Count(),
            interactionDays = evidence.Select(item => LocalDate(item.OccurredAt)).Distinct().Count(),
            evidenceCount = evidence.Length,
            personMentions = personRecords
                .Where(item => people.ContainsKey(item.PersonId))
                .GroupBy(item => item.PersonId)
                .Select(group => new
                {
                    name = people[group.Key].Name,
                    evidenceCount = group.Select(SourceKey).Where(key => key is not null).Distinct().Count(),
                })
                .ToArray(),
        };
        return Section(
            "Relationship",
            evidence.Length >= 2 && personRecords.Count > 0,
            metrics,
            new { userDisplayName = displayName, period, relationshipRecords = new { people = records, events = eventItems } },
            evidence);
    }

    /// <summary>构建认识回望输入和指标。</summary>
    private static ReportSectionInput BuildRecognitionSection(
        string displayName,
        object period,
        IReadOnlyList<Recognition> recognitions,
        IReadOnlyDictionary<string, SourceValue> sources,
        IReadOnlyDictionary<string, string> refs)
    {
        var sourceKeys = recognitions.Select(SourceKey)
            .Where(key => key is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var evidence = Evidence(sourceKeys, new HashSet<long>(), sources, refs, []);
        var records = recognitions.Select(item => new
        {
            item.Category,
            item.Content,
            item.Keywords,
            evidenceRef = Reference(item.SourceMessageId, item.SourceMomentId, refs),
        }).ToArray();
        var metrics = new
        {
            recordCount = recognitions.Count,
            coveredDays = evidence.Select(item => LocalDate(item.OccurredAt)).Distinct().Count(),
            categoryDistribution = recognitions
                .GroupBy(item => item.Category)
                .Select(group => new { category = group.Key, count = group.Count() })
                .ToArray(),
        };
        return Section(
            "Recognition",
            evidence.Length >= 2,
            metrics,
            new { userDisplayName = displayName, period, recognitionRecords = records },
            evidence);
    }

    /// <summary>构建情绪脉络输入和指标。</summary>
    private static ReportSectionInput BuildEmotionSection(
        string displayName,
        object period,
        IReadOnlyList<EmotionRecord> emotions,
        IReadOnlyList<CbtObservation> cbtObservations,
        IReadOnlyDictionary<string, SourceValue> sources,
        IReadOnlyDictionary<string, string> refs)
    {
        var sourceKeys = emotions.Select(SourceKey)
            .Concat(cbtObservations.Select(SourceKey))
            .Where(key => key is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var evidence = Evidence(sourceKeys, new HashSet<long>(), sources, refs, []);
        var records = emotions.Select(item => new
        {
            item.Family,
            item.Subtype,
            item.Intensity,
            item.Summary,
            item.OccurredAt,
            evidenceRef = Reference(item.SourceMessageId, item.SourceMomentId, refs),
        }).ToArray();
        var observations = cbtObservations.Select(item =>
        {
            var key = SourceKey(item);
            return new
            {
                item.Situation,
                item.AutomaticThought,
                item.BodySensation,
                item.Behavior,
                item.ImmediateOutcome,
                item.OccurredAt,
                emotions = emotions
                    .Where(emotion => SourceKey(emotion) == key)
                    .Select(emotion => new
                    {
                        emotion.Family,
                        emotion.Subtype,
                        emotion.Intensity,
                    })
                    .ToArray(),
                evidenceRef = Reference(item.SourceMessageId, item.SourceMomentId, refs),
            };
        }).ToArray();
        var distribution = emotions.GroupBy(item => item.Family).Select(group => new
        {
            family = group.Key,
            count = group.Count(),
            percentage = emotions.Count == 0 ? 0 : Math.Round(group.Count() * 100d / emotions.Count, 1),
            averageIntensity = Math.Round(group.Average(item => item.Intensity), 1),
        }).ToArray();
        var daily = emotions
            .GroupBy(item => LocalDate(item.OccurredAt))
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                date = group.Key,
                emotions = group.GroupBy(item => item.Family).Select(family => new
                {
                    family = family.Key,
                    count = family.Count(),
                    averageIntensity = Math.Round(family.Average(item => item.Intensity), 1),
                }).ToArray(),
            })
            .ToArray();
        var metrics = new
        {
            recordCount = emotions.Count,
            coveredDays = daily.Length,
            familyDistribution = distribution,
            dailyDistribution = daily,
            cbtObservationCount = cbtObservations.Count,
            cbtCoveredDays = cbtObservations.Select(item => LocalDate(item.OccurredAt)).Distinct().Count(),
            completeChainCount = cbtObservations.Count(item =>
                !string.IsNullOrWhiteSpace(item.Situation)
                && !string.IsNullOrWhiteSpace(item.AutomaticThought)
                && !string.IsNullOrWhiteSpace(item.Behavior)
                && !string.IsNullOrWhiteSpace(item.ImmediateOutcome)),
        };
        var cbtDays = cbtObservations.Select(item => LocalDate(item.OccurredAt)).Distinct().Count();
        return Section(
            "Emotion",
            evidence.Length >= 3 && daily.Length >= 2
                || cbtObservations.Count >= 2 && cbtDays >= 2,
            metrics,
            new
            {
                userDisplayName = displayName,
                period,
                emotionRecords = records,
                cbtObservations = observations,
            },
            evidence);
    }

    /// <summary>把指标、业务记录与公开证据拼成最终分区输入。</summary>
    private static ReportSectionInput Section(
        string kind,
        bool eligible,
        object metrics,
        object records,
        ReportEvidence[] evidence)
    {
        var metricsJson = JsonSerializer.Serialize(metrics, JsonOptions);
        var publicEvidence = evidence.Select(item => new
        {
            item.Ref,
            item.OccurredAt,
            item.SourceType,
            item.Text,
            item.AttachmentDescriptions,
        });
        var context = JsonSerializer.Serialize(new
        {
            records,
            metrics = JsonSerializer.Deserialize<JsonElement>(metricsJson),
            evidence = publicEvidence,
        }, JsonOptions);
        return new ReportSectionInput(
            kind,
            eligible,
            metricsJson,
            context,
            JsonSerializer.Serialize(evidence, JsonOptions),
            evidence.Select(item => item.Ref).ToArray());
    }

    /// <summary>把来源编号转换为分区独立的证据快照。</summary>
    private static ReportEvidence[] Evidence(
        IEnumerable<string> sourceKeys,
        IReadOnlySet<long> allowedAttachmentIds,
        IReadOnlyDictionary<string, SourceValue> sources,
        IReadOnlyDictionary<string, string> refs,
        IReadOnlyList<Attachment> attachments)
    {
        var attachmentById = attachments.ToDictionary(item => item.Id);
        return sourceKeys
            .Where(key => sources.ContainsKey(key) && refs.ContainsKey(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => sources[key].OccurredAt)
            .Select(key =>
            {
                var source = sources[key];
                var ids = source.AttachmentIds.Where(allowedAttachmentIds.Contains).Distinct().ToArray();
                return new ReportEvidence
                {
                    Ref = refs[key],
                    OccurredAt = source.OccurredAt,
                    SourceType = source.SourceType,
                    Text = source.Text,
                    MessageId = source.MessageId,
                    MomentId = source.MomentId,
                    AttachmentIds = ids,
                    AttachmentDescriptions = ids
                        .Where(attachmentById.ContainsKey)
                        .Select(id => attachmentById[id].AiDescription)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Cast<string>()
                        .ToArray(),
                };
            })
            .ToArray();
    }

    /// <summary>生成范围内消息和一刻的来源字典。</summary>
    private static Dictionary<string, SourceValue> SourceLookup(
        IReadOnlyList<ConversationMessage> messages,
        IReadOnlyList<Moment> moments,
        IReadOnlyList<Attachment> attachments)
    {
        var attachmentsByMessage = attachments
            .Where(item => item.MessageId.HasValue)
            .GroupBy(item => item.MessageId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToArray());
        var result = messages.ToDictionary(
            item => MessageKey(item.Id),
            item => new SourceValue(
                "Conversation",
                item.Text ?? string.Empty,
                item.CreatedAt,
                item.Id,
                null,
                attachmentsByMessage.GetValueOrDefault(item.Id) ?? []));
        foreach (var moment in moments)
            result[MomentKey(moment.Id)] = new SourceValue(
                "Moment",
                moment.Text ?? string.Empty,
                moment.PublishedAt,
                null,
                moment.Id,
                [moment.AttachmentId]);
        return result;
    }

    /// <summary>收集本周期所有会被任一分区使用的来源。</summary>
    private static string[] CollectSourceKeys(
        IReadOnlyList<ConversationMessage> messages,
        IReadOnlyList<Conversation> conversations,
        IReadOnlyList<PersonRecord> people,
        IReadOnlyList<PlaceRecord> places,
        IReadOnlyList<LifeEvent> events,
        IReadOnlyList<Recognition> recognitions,
        IReadOnlyList<EmotionRecord> emotions,
        IReadOnlyList<CbtObservation> cbtObservations)
    {
        var summarizedConversationIds = conversations
            .Where(item => !string.IsNullOrWhiteSpace(item.Summary))
            .Select(item => item.Id)
            .ToHashSet();
        return messages.Where(item => summarizedConversationIds.Contains(item.ConversationId))
            .Select(item => MessageKey(item.Id))
            .Concat(people.Select(SourceKey).Where(key => key is not null).Cast<string>())
            .Concat(places.Select(SourceKey).Where(key => key is not null).Cast<string>())
            .Concat(events.Select(SourceKey).Where(key => key is not null).Cast<string>())
            .Concat(recognitions.Select(SourceKey).Where(key => key is not null).Cast<string>())
            .Concat(emotions.Select(SourceKey).Where(key => key is not null).Cast<string>())
            .Concat(cbtObservations.Select(SourceKey).Where(key => key is not null).Cast<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>最近 14 天内的 WHO-5 结果可以交给综合智能体只读解释。</summary>
    private async Task<string?> LatestWellbeingAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-14);
        var item = await db.Queryable<WellbeingAssessment>()
            .Where(row => row.UserId == userId && row.AssessedAt >= since)
            .OrderBy(row => row.AssessedAt, OrderByType.Desc)
            .FirstAsync(cancellationToken);
        return item is null
            ? null
            : JsonSerializer.Serialize(new
            {
                item.AssessedAt,
                item.RawScore,
                item.PercentageScore,
                interpretation = item.PercentageScore < 50
                    ? "分数低于 50，只建议考虑进一步专业评估，不能据此诊断。"
                    : "分数越高代表过去两周自评的心理幸福感越高。",
            }, JsonOptions);
    }

    private static bool InRange(
        long? messageId,
        long? momentId,
        IReadOnlySet<long> messageIds,
        IReadOnlySet<long> momentIds) =>
        messageId.HasValue && messageIds.Contains(messageId.Value)
        || momentId.HasValue && momentIds.Contains(momentId.Value);

    private static string? SourceKey(PersonRecord item) => SourceKey(item.SourceMessageId, item.SourceMomentId);
    private static string? SourceKey(PlaceRecord item) => SourceKey(item.SourceMessageId, item.SourceMomentId);
    private static string? SourceKey(LifeEvent item) => SourceKey(item.SourceMessageId, item.SourceMomentId);
    private static string? SourceKey(Recognition item) => SourceKey(item.SourceMessageId, item.SourceMomentId);
    private static string? SourceKey(EmotionRecord item) => SourceKey(item.SourceMessageId, item.SourceMomentId);
    private static string? SourceKey(CbtObservation item) => SourceKey(item.SourceMessageId, item.SourceMomentId);
    private static string? SourceKey(long? messageId, long? momentId) =>
        messageId.HasValue ? MessageKey(messageId.Value)
        : momentId.HasValue ? MomentKey(momentId.Value)
        : null;

    private static string? Reference(
        long? messageId,
        long? momentId,
        IReadOnlyDictionary<string, string> refs)
    {
        var key = SourceKey(messageId, momentId);
        return key is null ? null : refs.GetValueOrDefault(key);
    }

    private static string MessageKey(long id) => $"message:{id}";
    private static string MomentKey(long id) => $"moment:{id}";

    private static DateOnly LocalDate(DateTimeOffset value) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, ShanghaiTimeZone).DateTime);

    /// <summary>把包含首尾的上海自然日范围转换为数据库查询使用的 UTC 半开区间。</summary>
    private static (DateTimeOffset Start, DateTimeOffset EndExclusive) ToUtcRange(
        DateOnly start,
        DateOnly end)
    {
        var startLocal = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endLocal = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            new DateTimeOffset(startLocal, ShanghaiTimeZone.GetUtcOffset(startLocal)).ToUniversalTime(),
            new DateTimeOffset(endLocal, ShanghaiTimeZone.GetUtcOffset(endLocal)).ToUniversalTime());
    }

    private sealed record SourceValue(
        string SourceType,
        string Text,
        DateTimeOffset OccurredAt,
        long? MessageId,
        long? MomentId,
        long[] AttachmentIds);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeZoneInfo ShanghaiTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
}
