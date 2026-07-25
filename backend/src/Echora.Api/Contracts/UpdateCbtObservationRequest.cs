namespace Echora.Api.Contracts;

/// <summary>用户对一条 CBT 自我观察的修订；原聊天或一刻不会被修改。</summary>
public sealed class UpdateCbtObservationRequest
{
    /// <summary>客观发生的情境。</summary>
    public string Situation { get; set; } = string.Empty;

    /// <summary>用户当时明确表达的即时想法。</summary>
    public string? AutomaticThought { get; set; }

    /// <summary>用户当时明确表达的身体感受。</summary>
    public string? BodySensation { get; set; }

    /// <summary>用户当时明确表达的做法或回避。</summary>
    public string? Behavior { get; set; }

    /// <summary>用户当时明确表达的直接结果。</summary>
    public string? ImmediateOutcome { get; set; }
}
