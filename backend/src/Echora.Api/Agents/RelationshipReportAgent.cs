using Echora.Api.Workflows;
using Microsoft.Extensions.AI;

namespace Echora.Api.Agents;

/// <summary>只分析指定日期内人物往来的报告子智能体。</summary>
public sealed class RelationshipReportAgent(
    StructuredAgentRunner runner,
    ILogger<RelationshipReportAgent> logger)
{
    /// <summary>根据人物记录、相关事件和原话生成一份带依据的人际往来。</summary>
    public async ValueTask<ReportBranchResult> RunAsync(
        HeartReportInput input,
        CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var section = input.Sections.Single(item => item.Kind == "Relationship");
            var dailyPeriod = ReportPeriod.IsDaily(input.PeriodType);
            var raw = await runner.RunAsync<ReportContent>(
                "relationship_report_agent",
                "根据给定人物往来、统计和原话依据生成人际往来报告。",
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
            return Success(content, startedAt);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Relationship report Agent failed: ReportPackId {ReportPackId}", input.ReportPackId);
            return Failure(startedAt);
        }
    }

    private static ReportBranchResult Success(ReportContent content, long startedAt) =>
        new("Relationship", true, content.ToJson(), null,
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    private static ReportBranchResult Failure(long startedAt) =>
        new("Relationship", false, null, "人际往来生成失败。",
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    private const string Instructions = """
        你负责回望指定日期内用户与其他人物的真实互动。
        只使用输入中的 relationshipRecords、metrics 和 evidence；不要使用常识补全，不要重新计算统计，不要调用工具。
        关注和谁互动、共同做了什么、用户明确表达了什么感受或期待。
        不计算亲密度、人物重要性或关系健康分，不猜测他人动机，不把一次互动写成稳定关系结论。
        每条 finding 必须引用输入中真实存在的 evidence ref；最多三条 finding、两个 reflectionQuestions。
        资料有限时把限制写进 uncertainties。

        只返回一个 JSON 对象，字段必须与下例完全一致，不要增加字段：
        {"headline":"朋友的陪伴是这段时间的重要内容","summary":"记录中多次出现与小明见面和交流的经历。","findings":[{"title":"和小明有两次明确互动","observation":"两条不同日期的原话分别记录了散步和再次见面，能够确认这段时间存在持续往来。","evidenceRefs":["E1","E4"],"confidence":"High"}],"reflectionQuestions":["哪一次互动最让你感到被理解？"],"uncertainties":["现有记录不能代表关系的全部状态。"]}
        """;
}
