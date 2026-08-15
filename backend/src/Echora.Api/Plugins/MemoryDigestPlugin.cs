using System.ComponentModel;
using Echora.Api.Services;

namespace Echora.Api.Plugins;

/// <summary>为 Agent 提供整篇可读的记忆档案。</summary>
public sealed class MemoryDigestPlugin(MemoryDigestRenderer renderer, long userId)
{
    /// <summary>读取一篇完整的记忆档案。</summary>
    [DisplayName("read_memory_digest")]
    [Description("读取整篇记忆档案。需要通篇了解某个人、情绪整体走势、认知脉络或近一年生活时间线时调用；只找某一件具体的事请用 search 工具。")]
    public Task<string> ReadAsync(
        [Description("档案类型：person（某个人的完整脉络）、emotion（情绪整体走势）、recognition（认知脉络）、timeline（近一年生活时间线）。")]
        string kind,
        [Description("当 kind 为 person 时必填，传人物称呼或别名；其他类型留空。")]
        string? name = null,
        CancellationToken cancellationToken = default) =>
        renderer.RenderAsync(userId, kind, name, cancellationToken);
}
