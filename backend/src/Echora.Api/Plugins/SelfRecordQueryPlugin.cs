using System.ComponentModel;
using System.Text.Json;
using Echora.Api.Entities;
using SqlSugar;

namespace Echora.Api.Plugins;

/// <summary>为 Agent 查询已经保存的用户认识、情绪和 CBT 自我观察。</summary>
public sealed class SelfRecordQueryPlugin(ISqlSugarClient db, long userId)
{
    /// <summary>查询可能与当前内容有关的用户认识、情绪和 CBT 自我观察。</summary>
    [DisplayName("search_self_records")]
    [Description("查询已经保存的个人认识、情绪，以及过去情境中的想法、身体感受、行为和结果。回答需要联系这些过去记录时调用。")]
    public async Task<string> SearchAsync(
        [Description("要查找的个人特征、经历感受、情绪或相关关键词；一次可以传多个。")]
        IReadOnlyList<string> queries,
        CancellationToken cancellationToken = default)
    {
        var terms = queries.Select(Normalize).Where(term => term.Length > 0).Distinct().ToArray();
        if (terms.Length == 0) return "[]";
        var recognitions = await db.Queryable<Recognition>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var emotions = await db.Queryable<EmotionRecord>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var observations = await db.Queryable<CbtObservation>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var results = terms.Select(term => new
        {
            query = term,
            matches = recognitions
                .Where(item => Contains(item.Category, term)
                    || Contains(item.Content, term)
                    || item.Keywords.Any(value => Contains(value, term)))
                .Select(item => (object)new
                {
                    type = "recognition",
                    category = item.Category,
                    content = item.Content,
                    time = item.CreatedAt.ToString("yyyy-MM-dd"),
                })
                .Concat(emotions
                    .Where(item => Contains(item.Family, term)
                        || Contains(item.Subtype, term)
                        || Contains(item.Summary, term))
                    .Select(item => (object)new
                    {
                        type = "emotion",
                        category = item.Family,
                        content = item.Summary,
                        time = item.OccurredAt.ToString("yyyy-MM-dd"),
                    }))
                .Concat(observations
                    .Where(item => Contains(item.Situation, term)
                        || Contains(item.AutomaticThought, term)
                        || Contains(item.BodySensation, term)
                        || Contains(item.Behavior, term)
                        || Contains(item.ImmediateOutcome, term))
                    .Select(item => (object)new
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
                    }))
                .ToArray(),
        });
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    /// <summary>执行不区分大小写的双向包含匹配。</summary>
    private static bool Contains(string? value, string term)
    {
        var normalized = Normalize(value);
        return normalized.Length > 0 && (normalized.Contains(term) || term.Contains(normalized));
    }

    /// <summary>去掉空白并统一大小写，供分类、内容和关键词匹配。</summary>
    private static string Normalize(string? value) =>
        string.Concat((value ?? string.Empty).Where(character => !char.IsWhiteSpace(character))).ToLowerInvariant();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
