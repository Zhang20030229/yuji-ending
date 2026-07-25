namespace Echora.Api.Contracts;

/// <summary>用户手动选择的心迹报告日期范围。</summary>
public sealed record GenerateReportRequest(
    string Preset,
    DateOnly? StartDate,
    DateOnly? EndDate);
