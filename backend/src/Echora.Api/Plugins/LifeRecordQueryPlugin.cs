using System.ComponentModel;
using Echora.Api.Services;

namespace Echora.Api.Plugins;

/// <summary>为 Agent 查询已经保存的生活记录。</summary>
public sealed class LifeRecordQueryPlugin(MemoryRetrievalService retrieval, long userId)
{
    /// <summary>参与本工具检索的记录类型。</summary>
    private static readonly string[] Scopes = ["person", "place", "event", "fragment"];

    /// <summary>查询可能与当前内容有关的人物、地点、事件和会话片段。</summary>
    [DisplayName("search_life_records")]
    [Description("查询已经保存的人物、地点、事件和会话片段。需要了解当前消息提到的对象或经历是否曾被记录时调用。")]
    public Task<string> SearchAsync(
        [Description("要查找的人物称呼、地点名称、事件或经历关键词；一次可以传多个。")]
        IReadOnlyList<string> queries,
        CancellationToken cancellationToken = default) =>
        retrieval.SearchAsync(userId, queries, Scopes, cancellationToken);
}
