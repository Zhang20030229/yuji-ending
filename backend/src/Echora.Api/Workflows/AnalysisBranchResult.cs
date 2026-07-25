namespace Echora.Api.Workflows;

/// <summary>一个后台 Subagent 的成功 JSON 或安全错误。</summary>
public sealed record AnalysisBranchResult(
    string Branch,
    bool Succeeded,
    string? Json,
    string? Error,
    long DurationMilliseconds);
