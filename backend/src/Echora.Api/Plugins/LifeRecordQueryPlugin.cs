using System.ComponentModel;
using System.Text.Json;
using Echora.Api.Entities;
using SqlSugar;

namespace Echora.Api.Plugins;

/// <summary>为 Agent 查询已经保存的生活记录。</summary>
public sealed class LifeRecordQueryPlugin(ISqlSugarClient db, long userId)
{
    /// <summary>查询可能与当前内容有关的人物、地点、事件和会话片段。</summary>
    [DisplayName("search_life_records")]
    [Description("查询已经保存的人物、地点、事件和会话片段。需要了解当前消息提到的对象或经历是否曾被记录时调用。")]
    public async Task<string> SearchAsync(
        [Description("要查找的人物称呼、地点名称、事件或经历关键词；一次可以传多个。")]
        IReadOnlyList<string> queries,
        CancellationToken cancellationToken = default)
    {
        var terms = queries.Select(Normalize).Where(term => term.Length > 0).Distinct().ToArray();
        if (terms.Length == 0) return "[]";

        // MVP 数据规模很小；一次读出所需字段后在内存做包含匹配，避免五套重复动态 SQL。
        var people = await db.Queryable<Person>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var personAliases = await db.Queryable<PersonAlias>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var personRecords = await db.Queryable<PersonRecord>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var places = await db.Queryable<Place>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var placeAliases = await db.Queryable<PlaceAlias>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var placeRecords = await db.Queryable<PlaceRecord>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var events = await db.Queryable<LifeEvent>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var conversations = await db.Queryable<Conversation>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);

        var results = terms.Select(term => new
        {
            query = term,
            matches = BuildMatches(
                term,
                people,
                personAliases,
                personRecords,
                places,
                placeAliases,
                placeRecords,
                events,
                conversations),
        });
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    /// <summary>为一个查询词构造固定返回结构，并返回全部匹配结果。</summary>
    private static object[] BuildMatches(
        string term,
        IReadOnlyList<Person> people,
        IReadOnlyList<PersonAlias> personAliases,
        IReadOnlyList<PersonRecord> personRecords,
        IReadOnlyList<Place> places,
        IReadOnlyList<PlaceAlias> placeAliases,
        IReadOnlyList<PlaceRecord> placeRecords,
        IReadOnlyList<LifeEvent> events,
        IReadOnlyList<Conversation> conversations)
    {
        var matches = new List<object>();
        foreach (var person in people)
        {
            var aliases = personAliases.Where(item => item.PersonId == person.Id).Select(item => item.Name).ToArray();
            var records = personRecords.Where(item => item.PersonId == person.Id).OrderByDescending(item => item.CreatedAt).ToArray();
            if (!Contains(person.Name, term)
                && !Contains(person.Relationship, term)
                && !person.RelationshipKeywords.Any(value => Contains(value, term))
                && !aliases.Any(value => Contains(value, term))
                && !records.Any(item => Contains(item.Summary, term))) continue;
            matches.Add(new
            {
                type = "person",
                name = person.Name,
                summary = records.FirstOrDefault()?.Summary ?? string.Join('、', person.RelationshipKeywords),
                time = records.FirstOrDefault()?.CreatedAt.ToString("yyyy-MM-dd"),
            });
        }
        foreach (var place in places)
        {
            var aliases = placeAliases.Where(item => item.PlaceId == place.Id).Select(item => item.Name).ToArray();
            var records = placeRecords.Where(item => item.PlaceId == place.Id).OrderByDescending(item => item.CreatedAt).ToArray();
            if (!Contains(place.Name, term)
                && !Contains(place.Province, term)
                && !Contains(place.City, term)
                && !aliases.Any(value => Contains(value, term))
                && !records.Any(item => Contains(item.Summary, term))) continue;
            matches.Add(new
            {
                type = "place",
                name = place.Name,
                summary = records.FirstOrDefault()?.Summary ?? string.Join(' ', new[] { place.Province, place.City }.Where(value => !string.IsNullOrWhiteSpace(value))),
                time = records.FirstOrDefault()?.CreatedAt.ToString("yyyy-MM-dd"),
                latitude = place.Latitude,
                longitude = place.Longitude,
            });
        }
        matches.AddRange(events.Where(item => Contains(item.Title, term) || Contains(item.Summary, term)).Select(item => new
        {
            type = "event",
            name = item.Title,
            summary = item.Summary,
            time = item.OccurredAt.ToString("yyyy-MM-dd"),
        }));
        matches.AddRange(conversations.Where(item => Contains(item.Title, term) || Contains(item.Summary, term)).Select(item => new
        {
            type = "fragment",
            name = item.Title,
            summary = item.Summary ?? string.Empty,
            time = item.CreatedAt.ToString("yyyy-MM-dd"),
        }));
        return matches.ToArray();
    }

    /// <summary>执行不区分大小写的双向包含匹配。</summary>
    private static bool Contains(string? value, string term)
    {
        var normalized = Normalize(value);
        return normalized.Length > 0 && (normalized.Contains(term) || term.Contains(normalized));
    }

    /// <summary>去掉空白并统一大小写，供名称和关键词匹配。</summary>
    private static string Normalize(string? value) =>
        string.Concat((value ?? string.Empty).Where(character => !char.IsWhiteSpace(character))).ToLowerInvariant();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
