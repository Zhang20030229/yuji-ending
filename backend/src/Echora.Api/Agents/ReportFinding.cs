using System.ComponentModel;

namespace Echora.Api.Agents;

/// <summary>心迹报告中一条带真实依据的观察。</summary>
[Description("一条只能由给定原话依据支持的观察。")]
public sealed class ReportFinding
{
    /// <summary>简短观察标题。</summary>
    [Description("简短观察标题。")]
    public string Title { get; set; } = string.Empty;

    /// <summary>事实或可能关联，不写未经证明的因果。</summary>
    [Description("观察到的事实或可能关联，不写未经证明的因果。")]
    public string Observation { get; set; } = string.Empty;

    /// <summary>支持本观察的临时原话编号。</summary>
    [Description("支持本观察的 evidenceRef，例如 E1；只能选输入中已有值。")]
    public string[] EvidenceRefs { get; set; } = [];

    /// <summary>只允许 High 或 Medium。</summary>
    [Description("把握程度，只允许 High 或 Medium。")]
    public string Confidence { get; set; } = "Medium";
}
