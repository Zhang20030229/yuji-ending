using System.Text.Json;
using Echora.Api.Workflows;
using Microsoft.Extensions.AI;

namespace Echora.Api.Agents;

/// <summary>只分析指定日期内生活经历的报告子智能体。</summary>
public sealed class LifeReportAgent(
    StructuredAgentRunner runner,
    ILogger<LifeReportAgent> logger)
{
    /// <summary>根据固定代码准备的生活记录生成一份带依据的生活回望。</summary>
    public async ValueTask<ReportBranchResult> RunAsync(
        HeartReportInput input,
        CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var section = input.Sections.Single(item => item.Kind == "Life");
            var dailyPeriod = ReportPeriod.IsDaily(input.PeriodType);
            var raw = await runner.RunAsync<ReportContent>(
                "life_report_agent",
                "根据给定生活记录、统计和原话依据生成生活回望。",
                dailyPeriod ? Instructions + ReportPeriod.DailyScopeSuffix : Instructions,
                [new ChatMessage(ChatRole.User, section.ContextJson)],
                [],
                cancellationToken);
            var content = ReportContent.Parse(
                raw,
                section.AllowedEvidenceRefs.ToHashSet(StringComparer.Ordinal),
                input.UserDisplayName,
                null,
                dailyPeriod);
            return Success("Life", content, startedAt);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Life report Agent failed: ReportPackId {ReportPackId}", input.ReportPackId);
            return Failure("Life", "生活回望生成失败。", startedAt);
        }
    }

    private static ReportBranchResult Success(string kind, ReportContent content, long startedAt) =>
        new(kind, true, content.ToJson(), null,
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    private static ReportBranchResult Failure(string kind, string error, long startedAt) =>
        new(kind, false, null, error,
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    private const string Instructions = """
        你负责回望指定日期内真实发生的生活经历。
        只使用输入中的 lifeRecords、metrics 和 evidence；不要使用常识补全，不要重新计算统计，不要调用工具。
        关注发生了什么、去了哪里、做了什么以及值得回看的生活片段。
        不把记录数量解释成生活质量，不写心理诊断，不把相关性写成因果。
        每条 finding 必须引用输入中真实存在的 evidence ref；最多三条 finding、两个 reflectionQuestions。
        资料有限时把限制写进 uncertainties，不要用空泛安慰填满报告。

        只返回一个 JSON 对象，字段必须与下例完全一致，不要增加字段：
        {"headline":"这一周留下了几段值得回看的经历","summary":"这段时间的记录主要围绕外出与朋友相处。","findings":[{"title":"和朋友一起外出","observation":"两条记录都提到与朋友共同外出，陪伴是这段时间反复出现的生活内容。","evidenceRefs":["E1","E3"],"confidence":"High"}],"reflectionQuestions":["哪一段经历最值得以后重新翻看？"],"uncertainties":["记录只覆盖了三个日期。"]}
        """;
}
