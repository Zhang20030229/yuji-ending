using System.Text.Json;
using Echora.Api.Data;
using Echora.Api.Entities;
using Echora.Api.ModelRuntime;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>向量语义检索与关键词召回的混合检索；向量不可用时自动退化为纯关键词。</summary>
public sealed class MemoryRetrievalService(
    ISqlSugarClient db,
    IMemoryEmbeddingClient client,
    ILogger<MemoryRetrievalService> logger)
{
    /// <summary>低于该余弦相似度的语义结果视为噪声。</summary>
    private const double MinimumVectorScore = 0.35;

    /// <summary>关键词命中是强信号，固定给一个高于语义阈值的分数。</summary>
    private const double KeywordScore = 0.60;

    /// <summary>每个查询词最终返回的最大条数。</summary>
    private const int DefaultTopN = 8;

    /// <summary>按查询词返回与现有工具完全一致的 JSON 结构。</summary>
    public async Task<string> SearchAsync(
        long userId,
        IReadOnlyList<string> queries,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queries);
        var terms = queries
            .Select(query => query?.Trim() ?? string.Empty)
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (terms.Length == 0) return "[]";

        // 白名单收敛，杜绝调用方传入未知 source_type。
        var allowed = scopes.Where(MemoryEmbeddingService.AllSourceTypes.Contains).ToArray();
        if (allowed.Length == 0) return "[]";

        var vectors = await TryEmbedQueriesAsync(terms, cancellationToken);
        var results = new List<object>(terms.Length);
        for (var index = 0; index < terms.Length; index++)
        {
            var hits = new Dictionary<(string SourceType, long SourceId), double>();
            if (vectors is not null)
            {
                foreach (var hit in await CollectVectorHitsAsync(userId, vectors[index], allowed, cancellationToken))
                    Accumulate(hits, hit.Key, hit.Value);
            }
            foreach (var hit in await CollectKeywordHitsAsync(userId, terms[index], allowed, cancellationToken))
                Accumulate(hits, hit, KeywordScore);

            var matches = await LoadMatchesAsync(userId, hits, cancellationToken);
            results.Add(new { query = terms[index], matches });
        }
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    /// <summary>向量化查询词；任何不可用都只降级为关键词检索，不影响本次回答。</summary>
    private async Task<IReadOnlyList<float[]>?> TryEmbedQueriesAsync(
        IReadOnlyList<string> terms,
        CancellationToken cancellationToken)
    {
        if (!client.IsEnabled || !VectorSchema.VectorSearchAvailable) return null;
        try
        {
            return await client.EmbedAsync(terms, EmbeddingPurpose.Query, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Memory retrieval fell back to keyword-only matching.");
            return null;
        }
    }

    /// <summary>pgvector 近邻检索；只在当前用户范围内查询。</summary>
    private async Task<Dictionary<(string, long), double>> CollectVectorHitsAsync(
        long userId,
        float[] vector,
        string[] scopes,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        // scopes 已经过白名单收敛，内联到 SQL 不引入注入面，且避免依赖驱动对数组参数的处理差异。
        var scopeList = string.Join(',', scopes.Select(scope => $"'{scope}'"));
        var rows = await db.Ado.SqlQueryAsync<VectorHitRow>(
            $"""
            SELECT source_type AS SourceType,
                   source_id   AS SourceId,
                   1 - (embedding <=> @query::vector) AS Score
            FROM memory_embeddings
            WHERE user_id = @userId AND source_type IN ({scopeList})
            ORDER BY embedding <=> @query::vector
            LIMIT @limit
            """,
            new
            {
                userId,
                query = MemoryEmbeddingService.FormatVector(vector),
                limit = DefaultTopN * 3,
            });
        return rows
            .Where(row => row.Score >= MinimumVectorScore)
            .ToDictionary(row => (row.SourceType, row.SourceId), row => row.Score);
    }

    /// <summary>同一来源在两个通道都命中时保留较高分。</summary>
    private static void Accumulate(
        Dictionary<(string SourceType, long SourceId), double> hits,
        (string SourceType, long SourceId) key,
        double score)
    {
        if (!hits.TryGetValue(key, out var existing) || score > existing) hits[key] = score;
    }

    /// <summary>越久远的记录轻微降权，但不会被完全埋没。</summary>
    private static double TimeDecay(DateTimeOffset? occurredAt, DateTimeOffset now)
    {
        if (occurredAt is null) return 1.0;
        var months = Math.Max(0, (now - occurredAt.Value).TotalDays / 30.44);
        return 1.0 / (1.0 + (months * 0.02));
    }

    private sealed class VectorHitRow
    {
        public string SourceType { get; set; } = string.Empty;
        public long SourceId { get; set; }
        public double Score { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>关键词通道：单向包含匹配下推到 SQL，避免全表读入内存。</summary>
    private async Task<List<(string SourceType, long SourceId)>> CollectKeywordHitsAsync(
        long userId,
        string term,
        string[] scopes,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var hits = new List<(string, long)>();
        var parameters = new { userId, pattern = $"%{EscapeLikePattern(term)}%", limit = DefaultTopN * 3 };
        foreach (var scope in scopes)
        {
            var sql = KeywordQueries.GetValueOrDefault(scope);
            if (sql is null) continue;
            var ids = await db.Ado.SqlQueryAsync<long>(sql, parameters);
            hits.AddRange(ids.Select(id => (scope, id)));
        }
        return hits;
    }

    /// <summary>转义 LIKE 通配符，保证用户词只做字面匹配。</summary>
    private static string EscapeLikePattern(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>七类记录的关键词召回语句；表名与字段均为常量，参数全部绑定。</summary>
    private static readonly Dictionary<string, string> KeywordQueries = new()
    {
        ["person"] =
            """
            SELECT DISTINCT p.id FROM people p
            LEFT JOIN person_aliases a ON a.person_id = p.id
            LEFT JOIN person_records r ON r.person_id = p.id
            WHERE p.user_id = @userId AND (
                p.name ILIKE @pattern
                OR p.relationship ILIKE @pattern
                OR EXISTS (SELECT 1 FROM unnest(p.relationship_keywords) k WHERE k ILIKE @pattern)
                OR a.name ILIKE @pattern
                OR r.summary ILIKE @pattern)
            LIMIT @limit
            """,
        ["place"] =
            """
            SELECT DISTINCT p.id FROM places p
            LEFT JOIN place_aliases a ON a.place_id = p.id
            LEFT JOIN place_records r ON r.place_id = p.id
            WHERE p.user_id = @userId AND (
                p.name ILIKE @pattern
                OR p.province ILIKE @pattern
                OR p.city ILIKE @pattern
                OR a.name ILIKE @pattern
                OR r.summary ILIKE @pattern)
            LIMIT @limit
            """,
        ["event"] =
            """
            SELECT id FROM life_events
            WHERE user_id = @userId AND (title ILIKE @pattern OR summary ILIKE @pattern)
            ORDER BY occurred_at DESC LIMIT @limit
            """,
        ["fragment"] =
            """
            SELECT id FROM conversations
            WHERE user_id = @userId AND (title ILIKE @pattern OR summary ILIKE @pattern)
            ORDER BY created_at DESC LIMIT @limit
            """,
        ["recognition"] =
            """
            SELECT id FROM recognitions
            WHERE user_id = @userId AND (
                category ILIKE @pattern
                OR content ILIKE @pattern
                OR EXISTS (SELECT 1 FROM unnest(keywords) k WHERE k ILIKE @pattern))
            ORDER BY created_at DESC LIMIT @limit
            """,
        ["emotion"] =
            """
            SELECT id FROM emotion_records
            WHERE user_id = @userId AND (family ILIKE @pattern OR subtype ILIKE @pattern OR summary ILIKE @pattern)
            ORDER BY occurred_at DESC LIMIT @limit
            """,
        ["cbt"] =
            """
            SELECT id FROM cbt_observations
            WHERE user_id = @userId AND (
                situation ILIKE @pattern
                OR automatic_thought ILIKE @pattern
                OR body_sensation ILIKE @pattern
                OR behavior ILIKE @pattern
                OR immediate_outcome ILIKE @pattern)
            ORDER BY occurred_at DESC LIMIT @limit
            """,
    };

    /// <summary>回查原表并按最终得分排序截断；查不到的命中静默丢弃。</summary>
    private async Task<object[]> LoadMatchesAsync(
        long userId,
        Dictionary<(string SourceType, long SourceId), double> hits,
        CancellationToken cancellationToken)
    {
        if (hits.Count == 0) return [];
        var now = DateTimeOffset.UtcNow;
        var scored = new List<(double Score, object Match)>();

        foreach (var group in hits.GroupBy(hit => hit.Key.SourceType))
        {
            var ids = group.Select(hit => hit.Key.SourceId).Distinct().ToArray();
            switch (group.Key)
            {
                case "person":
                {
                    var people = await db.Queryable<Person>()
                        .Where(item => item.UserId == userId && ids.Contains(item.Id))
                        .ToListAsync(cancellationToken);
                    var records = await db.Queryable<PersonRecord>()
                        .Where(item => item.UserId == userId && ids.Contains(item.PersonId))
                        .ToListAsync(cancellationToken);
                    foreach (var person in people)
                    {
                        var latest = records
                            .Where(item => item.PersonId == person.Id)
                            .OrderByDescending(item => item.CreatedAt)
                            .FirstOrDefault();
                        var occurredAt = latest?.CreatedAt ?? person.UpdatedAt;
                        scored.Add((
                            hits[("person", person.Id)] * TimeDecay(occurredAt, now),
                            new
                            {
                                type = "person",
                                name = person.Name,
                                summary = latest?.Summary ?? string.Join('、', person.RelationshipKeywords),
                                time = occurredAt.ToString("yyyy-MM-dd"),
                            }));
                    }
                    break;
                }
                case "place":
                {
                    var places = await db.Queryable<Place>()
                        .Where(item => item.UserId == userId && ids.Contains(item.Id))
                        .ToListAsync(cancellationToken);
                    var records = await db.Queryable<PlaceRecord>()
                        .Where(item => item.UserId == userId && ids.Contains(item.PlaceId))
                        .ToListAsync(cancellationToken);
                    foreach (var place in places)
                    {
                        var latest = records
                            .Where(item => item.PlaceId == place.Id)
                            .OrderByDescending(item => item.CreatedAt)
                            .FirstOrDefault();
                        var occurredAt = latest?.CreatedAt ?? place.UpdatedAt;
                        scored.Add((
                            hits[("place", place.Id)] * TimeDecay(occurredAt, now),
                            new
                            {
                                type = "place",
                                name = place.Name,
                                summary = latest?.Summary
                                          ?? string.Join(' ', new[] { place.Province, place.City }
                                              .Where(value => !string.IsNullOrWhiteSpace(value))),
                                time = occurredAt.ToString("yyyy-MM-dd"),
                                latitude = place.Latitude,
                                longitude = place.Longitude,
                            }));
                    }
                    break;
                }
                case "event":
                {
                    var events = await db.Queryable<LifeEvent>()
                        .Where(item => item.UserId == userId && ids.Contains(item.Id))
                        .ToListAsync(cancellationToken);
                    foreach (var item in events)
                    {
                        scored.Add((
                            hits[("event", item.Id)] * TimeDecay(item.OccurredAt, now),
                            new
                            {
                                type = "event",
                                name = item.Title,
                                summary = item.Summary,
                                time = item.OccurredAt.ToString("yyyy-MM-dd"),
                            }));
                    }
                    break;
                }
                case "fragment":
                {
                    var conversations = await db.Queryable<Conversation>()
                        .Where(item => item.UserId == userId && ids.Contains(item.Id))
                        .ToListAsync(cancellationToken);
                    foreach (var item in conversations)
                    {
                        scored.Add((
                            hits[("fragment", item.Id)] * TimeDecay(item.CreatedAt, now),
                            new
                            {
                                type = "fragment",
                                name = item.Title,
                                summary = item.Summary ?? string.Empty,
                                time = item.CreatedAt.ToString("yyyy-MM-dd"),
                            }));
                    }
                    break;
                }
                case "recognition":
                {
                    var recognitions = await db.Queryable<Recognition>()
                        .Where(item => item.UserId == userId && ids.Contains(item.Id))
                        .ToListAsync(cancellationToken);
                    foreach (var item in recognitions)
                    {
                        scored.Add((
                            hits[("recognition", item.Id)] * TimeDecay(item.CreatedAt, now),
                            new
                            {
                                type = "recognition",
                                category = item.Category,
                                content = item.Content,
                                time = item.CreatedAt.ToString("yyyy-MM-dd"),
                            }));
                    }
                    break;
                }
                case "emotion":
                {
                    var emotions = await db.Queryable<EmotionRecord>()
                        .Where(item => item.UserId == userId && ids.Contains(item.Id))
                        .ToListAsync(cancellationToken);
                    foreach (var item in emotions)
                    {
                        scored.Add((
                            hits[("emotion", item.Id)] * TimeDecay(item.OccurredAt, now),
                            new
                            {
                                type = "emotion",
                                category = item.Family,
                                content = item.Summary,
                                time = item.OccurredAt.ToString("yyyy-MM-dd"),
                            }));
                    }
                    break;
                }
                case "cbt":
                {
                    var observations = await db.Queryable<CbtObservation>()
                        .Where(item => item.UserId == userId && ids.Contains(item.Id))
                        .ToListAsync(cancellationToken);
                    foreach (var item in observations)
                    {
                        scored.Add((
                            hits[("cbt", item.Id)] * TimeDecay(item.OccurredAt, now),
                            new
                            {
                                type = "cbtObservation",
                                category = "情境—想法—行为",
                                content = string.Join("；", new[]
                                {
                                    item.Situation,
                                    item.AutomaticThought,
                                    item.BodySensation,
                                    item.Behavior,
                                    item.ImmediateOutcome,
                                }.Where(value => !string.IsNullOrWhiteSpace(value))),
                                time = item.OccurredAt.ToString("yyyy-MM-dd"),
                            }));
                    }
                    break;
                }
            }
        }

        return [.. scored.OrderByDescending(entry => entry.Score).Take(DefaultTopN).Select(entry => entry.Match)];
    }
}
