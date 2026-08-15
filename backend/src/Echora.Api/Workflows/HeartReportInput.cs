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

/// <summary>报告周期相关的共享判定，供服务层与智能体层复用。</summary>
public static class ReportPeriod
{
    public const string Daily = "Daily";

    /// <summary>是否为只覆盖一天的日报告。</summary>
    public static bool IsDaily(string periodType) =>
        string.Equals(periodType, Daily, StringComparison.Ordinal);

    /// <summary>综合心迹所需的最少已完成分区数；日报告常只有一个分区成立。</summary>
    public static int MinimumComposableSections(string periodType) =>
        IsDaily(periodType) ? 1 : 2;

    /// <summary>日报告追加给子智能体的口径约束：只有一天资料，禁止升格为长期倾向。</summary>
    public const string DailyScopeSuffix = """

        本次只覆盖今天一天，而且今天可能还没结束。
        所有表述必须限定在今天，禁止出现「这段时间」「这一周」「反复」「一直」「总是」「模式」「习惯性」等跨日或长期措辞。
        没有记录的时段一律不解释为平静、稳定或好转，也不由今天一次记录推断性格、能力或长期状态。
        """;
}
