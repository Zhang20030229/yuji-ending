using System.Text;
using Echora.Api.Entities;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>把结构化记忆按需渲染成可整篇阅读的 Markdown；不落表、不调模型。</summary>
public sealed class MemoryDigestRenderer(ISqlSugarClient db)
{
    /// <summary>允许的档案类型。</summary>
    public static readonly string[] Kinds = ["person", "emotion", "recognition", "timeline"];

    /// <summary>单篇档案上限，避免整篇阅读把上下文顶爆。</summary>
    private const int MaxLength = 6000;

    /// <summary>渲染一篇档案；类型非法或没有数据时返回明确说明。</summary>
    public async Task<string> RenderAsync(
        long userId,
        string kind,
        string? name,
        CancellationToken cancellationToken = default)
    {
        var normalized = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (!Kinds.Contains(normalized))
            return $"不支持的档案类型。可用类型：{string.Join('、', Kinds)}。";

        var markdown = normalized switch
        {
            "person" => await RenderPersonAsync(userId, name, cancellationToken),
            "emotion" => await RenderEmotionAsync(userId, cancellationToken),
            "recognition" => await RenderRecognitionAsync(userId, cancellationToken),
            "timeline" => await RenderTimelineAsync(userId, cancellationToken),
            _ => string.Empty,
        };
        return Truncate(markdown);
    }

    /// <summary>一个人物的完整脉络：关系、别名、全部记录、关联事件与同期情绪。</summary>
    private async Task<string> RenderPersonAsync(long userId, string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name)) return "查询人物档案需要提供人物名称。";
        // 名称来自模型参数，必须转义 LIKE 通配符后只做字面匹配。
        var escaped = name.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var pattern = $"%{escaped}%";
        var person = await db.Queryable<Person>()
            .Where(item => item.UserId == userId)
            .Where($"name ILIKE @pattern OR normalized_name ILIKE @pattern", new { pattern })
            .OrderBy(item => item.UpdatedAt, OrderByType.Desc)
            .FirstAsync(cancellationToken);
        if (person is null)
        {
            var aliasPersonId = await db.Queryable<PersonAlias>()
                .Where(item => item.UserId == userId)
                .Where($"name ILIKE @pattern", new { pattern })
                .Select(item => item.PersonId)
                .FirstAsync(cancellationToken);
            if (aliasPersonId == 0) return $"没有找到与“{name}”对应的人物记录。";
            person = await db.Queryable<Person>()
                .Where(item => item.UserId == userId && item.Id == aliasPersonId)
                .FirstAsync(cancellationToken);
            if (person is null) return $"没有找到与“{name}”对应的人物记录。";
        }

        var aliases = await db.Queryable<PersonAlias>()
            .Where(item => item.UserId == userId && item.PersonId == person.Id)
            .Select(item => item.Name)
            .ToListAsync(cancellationToken);
        var records = await db.Queryable<PersonRecord>()
            .Where(item => item.UserId == userId && item.PersonId == person.Id)
            .OrderBy(item => item.CreatedAt, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        var events = await db.Queryable<LifeEvent>()
            .Where(item => item.UserId == userId)
            .Where($"@personId = ANY(person_ids)", new { personId = person.Id })
            .OrderBy(item => item.OccurredAt, OrderByType.Desc)
            .Take(30)
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine($"# 人物档案：{person.Name}");
        builder.AppendLine();
        builder.AppendLine($"- 关系：{person.Relationship}");
        if (person.RelationshipKeywords.Length > 0)
            builder.AppendLine($"- 关系关键词：{string.Join('、', person.RelationshipKeywords)}");
        if (aliases.Count > 0)
            builder.AppendLine($"- 别名：{string.Join('、', aliases)}");
        builder.AppendLine($"- 记录条数：{records.Count}");
        builder.AppendLine();

        builder.AppendLine("## 记录时间线");
        if (records.Count == 0) builder.AppendLine("暂无记录。");
        foreach (var record in records)
            builder.AppendLine($"- {record.CreatedAt:yyyy-MM-dd}　{record.Summary}");
        builder.AppendLine();

        builder.AppendLine("## 关联事件");
        if (events.Count == 0) builder.AppendLine("暂无关联事件。");
        foreach (var item in events)
            builder.AppendLine($"- {item.OccurredAt:yyyy-MM-dd}　{item.Title}：{item.Summary}");

        return builder.ToString();
    }

    /// <summary>按情绪家族分组的强度分布与代表性摘要。</summary>
    private async Task<string> RenderEmotionAsync(long userId, CancellationToken cancellationToken)
    {
        var emotions = await db.Queryable<EmotionRecord>()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.OccurredAt, OrderByType.Desc)
            .Take(60)
            .ToListAsync(cancellationToken);
        if (emotions.Count == 0) return "# 情绪档案\n\n暂无情绪记录。";

        var builder = new StringBuilder();
        builder.AppendLine("# 情绪档案");
        builder.AppendLine();
        builder.AppendLine($"覆盖最近 {emotions.Count} 条记录，时间范围 {emotions[^1].OccurredAt:yyyy-MM-dd} 至 {emotions[0].OccurredAt:yyyy-MM-dd}。");
        builder.AppendLine();
        foreach (var family in emotions.GroupBy(item => item.Family).OrderByDescending(group => group.Count()))
        {
            var items = family.OrderByDescending(item => item.OccurredAt).ToArray();
            builder.AppendLine($"## {family.Key}（{items.Length} 次，平均强度 {items.Average(item => item.Intensity):0.0}）");
            foreach (var item in items)
                builder.AppendLine($"- {item.OccurredAt:yyyy-MM-dd}　{item.Subtype}·强度{item.Intensity}　{item.Summary}");
            builder.AppendLine();
        }
        return builder.ToString();
    }

    /// <summary>按认识分类分组的认知脉络。</summary>
    private async Task<string> RenderRecognitionAsync(long userId, CancellationToken cancellationToken)
    {
        var recognitions = await db.Queryable<Recognition>()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.CreatedAt, OrderByType.Desc)
            .Take(80)
            .ToListAsync(cancellationToken);
        if (recognitions.Count == 0) return "# 认知档案\n\n暂无认识记录。";

        var builder = new StringBuilder();
        builder.AppendLine("# 认知档案");
        builder.AppendLine();
        foreach (var category in recognitions.GroupBy(item => item.Category).OrderByDescending(group => group.Count()))
        {
            builder.AppendLine($"## {category.Key}（{category.Count()} 条）");
            foreach (var item in category.OrderByDescending(entry => entry.CreatedAt))
                builder.AppendLine($"- {item.CreatedAt:yyyy-MM-dd}　{item.Content}");
            builder.AppendLine();
        }
        return builder.ToString();
    }

    /// <summary>近 12 个月按月分组的事件与一刻。</summary>
    private async Task<string> RenderTimelineAsync(long userId, CancellationToken cancellationToken)
    {
        var since = DateTimeOffset.UtcNow.AddMonths(-12);
        var events = await db.Queryable<LifeEvent>()
            .Where(item => item.UserId == userId && item.OccurredAt >= since)
            .OrderBy(item => item.OccurredAt, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        var moments = await db.Queryable<Moment>()
            .Where(item => item.UserId == userId && item.PublishedAt >= since)
            .OrderBy(item => item.PublishedAt, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        if (events.Count == 0 && moments.Count == 0) return "# 生活时间线\n\n近一年暂无事件或一刻记录。";

        var entries = events
            .Select(item => (Time: item.OccurredAt, Line: $"事件　{item.Title}：{item.Summary}"))
            .Concat(moments.Select(item => (
                Time: item.PublishedAt,
                Line: $"一刻　{item.Title ?? item.Text ?? "（无标题）"}")))
            .OrderByDescending(entry => entry.Time)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("# 生活时间线（近 12 个月）");
        builder.AppendLine();
        foreach (var month in entries.GroupBy(entry => entry.Time.ToString("yyyy-MM")))
        {
            builder.AppendLine($"## {month.Key}");
            foreach (var entry in month)
                builder.AppendLine($"- {entry.Time:MM-dd}　{entry.Line}");
            builder.AppendLine();
        }
        return builder.ToString();
    }

    /// <summary>超长时按时间顺序截断并明确标注，避免模型误以为这是全部。</summary>
    private static string Truncate(string markdown) =>
        markdown.Length <= MaxLength
            ? markdown
            : string.Concat(markdown.AsSpan(0, MaxLength), "\n\n（更早内容已省略）");
}
