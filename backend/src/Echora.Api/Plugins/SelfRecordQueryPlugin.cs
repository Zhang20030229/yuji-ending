using System.ComponentModel;
using Echora.Api.Services;

namespace Echora.Api.Plugins;

/// <summary>为 Agent 查询已经保存的用户认识、情绪和 CBT 自我观察。</summary>
public sealed class SelfRecordQueryPlugin(MemoryRetrievalService retrieval, long userId)
{
    /// <summary>参与本工具检索的记录类型。</summary>
    private static readonly string[] Scopes = ["recognition", "emotion", "cbt"];

    /// <summary>查询可能与当前内容有关的用户认识、情绪和 CBT 自我观察。</summary>
    [DisplayName("search_self_records")]
    [Description("查询已经保存的个人认识、情绪，以及过去情境中的想法、身体感受、行为和结果。回答需要联系这些过去记录时调用。")]
    public Task<string> SearchAsync(
        [Description("要查找的个人特征、经历感受、情绪或相关关键词；一次可以传多个。")]
        IReadOnlyList<string> queries,
        CancellationToken cancellationToken = default) =>
        retrieval.SearchAsync(userId, queries, Scopes, cancellationToken);
}
