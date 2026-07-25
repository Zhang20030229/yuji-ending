namespace Echora.Api.Workflows;

/// <summary>一个报告子智能体或综合智能体的独立结果。</summary>
public sealed record ReportBranchResult(
    string Kind,
    bool Succeeded,
    string? ContentJson,
    string? Error,
    long DurationMilliseconds);
