using System.ComponentModel;

namespace Echora.Api.Agents;

/// <summary>报告中有真实依据的有帮助应对。</summary>
[Description("记录中已经出现、且可能有帮助的一种应对。")]
public sealed class HelpfulResponse
{
    /// <summary>对已发生应对及其后续的克制描述。</summary>
    [Description("描述已经发生的应对及其后续，不承诺效果。")]
    public string Observation { get; set; } = string.Empty;

    /// <summary>支持该观察的真实来源编号。</summary>
    [Description("一个或多个真实 evidence ref。")]
    public string[] EvidenceRefs { get; set; } = [];
}
