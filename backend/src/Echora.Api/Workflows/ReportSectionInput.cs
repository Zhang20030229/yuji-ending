namespace Echora.Api.Workflows;

/// <summary>一个报告分区已经由固定代码准备好的完整输入。</summary>
public sealed record ReportSectionInput(
    string Kind,
    bool Eligible,
    string MetricsJson,
    string ContextJson,
    string EvidenceJson,
    string[] AllowedEvidenceRefs);
