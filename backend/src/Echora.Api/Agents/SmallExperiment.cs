using System.ComponentModel;

namespace Echora.Api.Agents;

/// <summary>基于现有记录提出的一个低风险、可执行小实验。</summary>
[Description("一个具体、低风险、可执行且不承诺治疗效果的小实验。")]
public sealed class SmallExperiment
{
    /// <summary>小实验的短标题。</summary>
    [Description("小实验的简短中文标题。")]
    public string Title { get; set; } = string.Empty;

    /// <summary>用户下一次可以尝试的具体小行动。</summary>
    [Description("下一次可以尝试的一个具体小行动。")]
    public string Action { get; set; } = string.Empty;

    /// <summary>行动后用于自我观察的一个问题。</summary>
    [Description("行动后用于观察变化的一个问题。")]
    public string ReflectionQuestion { get; set; } = string.Empty;
}
