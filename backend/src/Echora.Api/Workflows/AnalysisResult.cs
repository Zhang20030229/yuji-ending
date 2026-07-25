namespace Echora.Api.Workflows;

/// <summary>AnalysisWorkflow 汇总的独立分支结果。</summary>
public sealed record AnalysisResult(IReadOnlyList<AnalysisBranchResult> Branches);
