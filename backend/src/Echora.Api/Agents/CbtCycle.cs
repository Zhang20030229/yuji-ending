using System.ComponentModel;

namespace Echora.Api.Agents;

/// <summary>心迹报告中由跨日期依据支持的一种可能循环。</summary>
[Description("由至少两个不同日期的真实来源支持的一种可能循环。")]
public sealed class CbtCycle
{
    /// <summary>循环的简短标题。</summary>
    [Description("循环的简短中文标题。")]
    public string Title { get; set; } = string.Empty;

    /// <summary>克制描述情境、想法、感受、行为和结果怎样共同出现。</summary>
    [Description("只描述记录中共同出现的内容，不断言因果。")]
    public string Observation { get; set; } = string.Empty;

    /// <summary>至少两个不同日期的真实来源编号。</summary>
    [Description("至少两个不同日期的真实 evidence ref。")]
    public string[] EvidenceRefs { get; set; } = [];
}
