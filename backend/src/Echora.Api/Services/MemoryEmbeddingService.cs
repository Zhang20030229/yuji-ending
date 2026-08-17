using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Echora.Api.Data;
using Echora.Api.Entities;
using Echora.Api.ModelRuntime;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>把结构化记忆记录同步成 memory_embeddings 中的向量。</summary>
public sealed class MemoryEmbeddingService(
    ISqlSugarClient db,
    IMemoryEmbeddingClient client,
    ILogger<MemoryEmbeddingService> logger)
{
    /// <summary>参与向量化与检索的记录类型白名单。</summary>
    public static readonly string[] AllSourceTypes =
        ["person", "place", "event", "fragment", "recognition", "emotion", "cbt"];

    /// <summary>把当前用户所有缺失或过期的向量补齐；返回新写入的条数。</summary>
    public async Task<int> SynchronizeAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (!client.IsEnabled || !VectorSchema.VectorSearchAvailable)
        {
            logger.LogInformation(
                "Memory embedding skipped: EmbeddingEnabled {EmbeddingEnabled}, VectorSearchAvailable {VectorSearchAvailable}.",
                client.IsEnabled,
                VectorSchema.VectorSearchAvailable);
            return 0;
        }

        var sources = await BuildSourcesAsync(userId, cancellationToken);
        var existing = await db.Ado.SqlQueryAsync<ExistingEmbedding>(
            "SELECT source_type AS SourceType, source_id AS SourceId, text_hash AS TextHash, model_id AS ModelId "
            + "FROM memory_embeddings WHERE user_id = @userId",
            new { userId });
        var index = existing.ToDictionary(row => (row.SourceType, row.SourceId));

        var pending = sources
            .Where(source => !index.TryGetValue((source.SourceType, source.SourceId), out var row)
                             || row.TextHash != source.TextHash
                             || row.ModelId != client.ModelId)
            .ToList();
        if (pending.Count == 0) return 0;

        var vectors = await client.EmbedAsync(
            [.. pending.Select(source => source.Content)],
            EmbeddingPurpose.Document,
            cancellationToken);

        for (var position = 0; position < pending.Count; position++)
            await UpsertAsync(userId, pending[position], vectors[position], cancellationToken);

        logger.LogInformation(
            "Memory embedding synchronized: UserId {UserId}, Written {Written}, Total {Total}.",
            userId,
            pending.Count,
            sources.Count);
        return pending.Count;
    }

    /// <summary>删除源记录已经不存在的向量行，避免账号或会话删除后留下孤儿。</summary>
    public async Task<int> RemoveOrphansAsync(CancellationToken cancellationToken = default)
    {
        if (!VectorSchema.VectorSearchAvailable) return 0;
        var removed = 0;
        foreach (var (sourceType, table) in OrphanSourceTables)
        {
            removed += await db.Ado.ExecuteCommandAsync(
                $"""
                DELETE FROM memory_embeddings m
                WHERE m.source_type = @sourceType
                  AND NOT EXISTS (SELECT 1 FROM {table} s WHERE s.id = m.source_id AND s.user_id = m.user_id);
                """,
                new { sourceType });
        }
        if (removed > 0)
            logger.LogInformation("Memory embedding orphans removed: Count {Count}.", removed);
        return removed;
    }

    /// <summary>删除单条来源的向量，用于用户主动驳回后立即退出语义召回。</summary>
    public async Task RemoveAsync(
        string sourceType,
        long sourceId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        if (!VectorSchema.VectorSearchAvailable) return;
        await db.Ado.ExecuteCommandAsync(
            """
            DELETE FROM memory_embeddings
            WHERE user_id = @userId AND source_type = @sourceType AND source_id = @sourceId;
            """,
            new { userId, sourceType, sourceId });
    }

    /// <summary>把一条向量幂等写入；同一来源重复同步只更新。</summary>
    private Task<int> UpsertAsync(
        long userId,
        MemorySource source,
        float[] vector,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return db.Ado.ExecuteCommandAsync(
            """
            INSERT INTO memory_embeddings
                (user_id, source_type, source_id, content, text_hash, model_id, embedding, occurred_at, updated_at)
            VALUES
                (@userId, @sourceType, @sourceId, @content, @textHash, @modelId, @embedding::vector, @occurredAt, now())
            ON CONFLICT (user_id, source_type, source_id) DO UPDATE SET
                content = EXCLUDED.content,
                text_hash = EXCLUDED.text_hash,
                model_id = EXCLUDED.model_id,
                embedding = EXCLUDED.embedding,
                occurred_at = EXCLUDED.occurred_at,
                updated_at = now();
            """,
            new
            {
                userId,
                sourceType = source.SourceType,
                sourceId = source.SourceId,
                content = source.Content,
                textHash = source.TextHash,
                modelId = client.ModelId,
                embedding = FormatVector(vector),
                occurredAt = source.OccurredAt,
            });
    }

    /// <summary>pgvector 文本字面量；必须使用不变文化，避免小数点被本地化。</summary>
    internal static string FormatVector(float[] vector) =>
        string.Concat(
            "[",
            string.Join(',', vector.Select(value => value.ToString("R", CultureInfo.InvariantCulture))),
            "]");

    /// <summary>内容任一处变化都会改变哈希，从而触发重新向量化。</summary>
    private static string ComputeHash(string sourceType, string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceType}\n{content}")));

    /// <summary>清理空白后拼接非空片段，避免生成只有分隔符的文本。</summary>
    private static string Join(params string?[] parts) =>
        string.Join("；", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));

    /// <summary>孤儿清理需要的源表映射；表名为常量，不来自外部输入。</summary>
    private static readonly (string SourceType, string Table)[] OrphanSourceTables =
    [
        ("person", "people"),
        ("place", "places"),
        ("event", "life_events"),
        ("fragment", "conversations"),
        ("recognition", "recognitions"),
        ("emotion", "emotion_records"),
        ("cbt", "cbt_observations"),
    ];

    private sealed class ExistingEmbedding
    {
        public string SourceType { get; set; } = string.Empty;
        public long SourceId { get; set; }
        public string TextHash { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
    }

    /// <summary>一条待向量化的记忆来源。</summary>
    internal sealed record MemorySource(string SourceType, long SourceId, string Content, DateTimeOffset? OccurredAt)
    {
        /// <summary>内容哈希，作为幂等判据。</summary>
        public string TextHash { get; } = ComputeHash(SourceType, Content);
    }

    /// <summary>把当前用户的七类记录渲染成可向量化的自然语言文本。</summary>
    private async Task<List<MemorySource>> BuildSourcesAsync(long userId, CancellationToken cancellationToken)
    {
        var sources = new List<MemorySource>();

        var people = await db.Queryable<Person>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var personAliases = await db.Queryable<PersonAlias>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var personRecords = await db.Queryable<PersonRecord>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        foreach (var person in people)
        {
            var aliases = personAliases.Where(item => item.PersonId == person.Id).Select(item => item.Name).ToArray();
            var records = personRecords
                .Where(item => item.PersonId == person.Id)
                .OrderByDescending(item => item.CreatedAt)
                .ToArray();
            sources.Add(new MemorySource(
                "person",
                person.Id,
                Join(
                    $"人物：{person.Name}（{person.Relationship}）",
                    person.RelationshipKeywords.Length > 0 ? $"关系关键词：{string.Join('、', person.RelationshipKeywords)}" : null,
                    aliases.Length > 0 ? $"别名：{string.Join('、', aliases)}" : null,
                    records.Length > 0 ? $"最近记录：{string.Join(' ', records.Take(3).Select(item => item.Summary))}" : null),
                records.FirstOrDefault()?.CreatedAt ?? person.UpdatedAt));
        }

        var places = await db.Queryable<Place>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var placeAliases = await db.Queryable<PlaceAlias>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var placeRecords = await db.Queryable<PlaceRecord>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        foreach (var place in places)
        {
            var aliases = placeAliases.Where(item => item.PlaceId == place.Id).Select(item => item.Name).ToArray();
            var records = placeRecords
                .Where(item => item.PlaceId == place.Id)
                .OrderByDescending(item => item.CreatedAt)
                .ToArray();
            var region = string.Concat(place.Province, place.City);
            sources.Add(new MemorySource(
                "place",
                place.Id,
                Join(
                    string.IsNullOrWhiteSpace(region) ? $"地点：{place.Name}" : $"地点：{place.Name}（{region}）",
                    aliases.Length > 0 ? $"别名：{string.Join('、', aliases)}" : null,
                    records.Length > 0 ? $"相关记录：{string.Join(' ', records.Take(3).Select(item => item.Summary))}" : null),
                records.FirstOrDefault()?.CreatedAt ?? place.UpdatedAt));
        }

        var events = await db.Queryable<LifeEvent>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        sources.AddRange(events.Select(item => new MemorySource(
            "event",
            item.Id,
            Join($"事件：{item.Title}", item.Summary),
            item.OccurredAt)));

        // 只有已经生成片段总结的会话才具备可检索语义。
        var conversations = await db.Queryable<Conversation>()
            .Where(item => item.UserId == userId && item.Summary != null)
            .ToListAsync(cancellationToken);
        sources.AddRange(conversations.Select(item => new MemorySource(
            "fragment",
            item.Id,
            Join($"会话片段：{item.Title}", item.Summary),
            item.LastMessageAt ?? item.CreatedAt)));

        // 被用户驳回的认识不再进入语义召回池，否则回填任务会把删掉的向量重新写回。
        var recognitions = await db.Queryable<Recognition>()
            .Where(item => item.UserId == userId && item.RejectedAt == null)
            .ToListAsync(cancellationToken);
        sources.AddRange(recognitions.Select(item => new MemorySource(
            "recognition",
            item.Id,
            Join(
                $"认识（{item.Category}）：{item.Content}",
                item.Keywords.Length > 0 ? $"关键词：{string.Join('、', item.Keywords)}" : null),
            item.CreatedAt)));

        var emotions = await db.Queryable<EmotionRecord>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        sources.AddRange(emotions.Select(item => new MemorySource(
            "emotion",
            item.Id,
            $"情绪（{item.Family}·{item.Subtype}，强度{item.Intensity}）：{item.Summary}",
            item.OccurredAt)));

        var observations = await db.Queryable<CbtObservation>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        sources.AddRange(observations.Select(item => new MemorySource(
            "cbt",
            item.Id,
            Join(
                $"情境：{item.Situation}",
                item.AutomaticThought is null ? null : $"想法：{item.AutomaticThought}",
                item.BodySensation is null ? null : $"身体感受：{item.BodySensation}",
                item.Behavior is null ? null : $"行为：{item.Behavior}",
                item.ImmediateOutcome is null ? null : $"结果：{item.ImmediateOutcome}"),
            item.OccurredAt)));

        // 空内容无法产生有意义的向量，直接排除。
        return [.. sources.Where(source => !string.IsNullOrWhiteSpace(source.Content))];
    }
}
