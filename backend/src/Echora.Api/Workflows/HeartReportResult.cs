namespace Echora.Api.Workflows;

/// <summary>四个子报告与可选综合心迹的工作流输出。</summary>
public sealed record HeartReportResult(
    IReadOnlyList<ReportBranchResult> Sections,
    ReportBranchResult? Overall);
