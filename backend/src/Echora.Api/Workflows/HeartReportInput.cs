namespace Echora.Api.Workflows;

/// <summary>一次心迹报告工作流需要的全部确定性输入。</summary>
public sealed record HeartReportInput(
    long ReportPackId,
    long UserId,
    string UserDisplayName,
    string PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    ReportSectionInput[] Sections,
    string? LatestWellbeingJson);
