using Echora.Api.Workflows;
using Microsoft.Extensions.AI;

namespace Echora.Api.Agents;

/// <summary>只分析指定日期内新增用户认识的报告子智能体。</summary>
public sealed class RecognitionReportAgent(
    StructuredAgentRunner runner,
    ILogger<RecognitionReportAgent> logger)
{
    /// <summary>根据认识记录和原话生成一份带依据的自我认识回望。</summary>
    public async ValueTask<ReportBranchResult> RunAsync(
        HeartReportInput input,
        CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var section = input.Sections.Single(item => item.Kind == "Recognition");
            var raw = await runner.RunAsync<ReportContent>(
                "recognition_report_agent",
                "根据给定认识记录、统计和原话依据生成自我认识回望。",
                Instructions,
                [new ChatMessage(ChatRole.User, section.ContextJson)],
                [],
                cancellationToken);
            var content = ReportContent.Parse(
                raw,
                section.AllowedEvidenceRefs.ToHashSet(StringComparer.Ordinal),
                input.UserDisplayName);
            return Success(content, startedAt);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Recognition report Agent failed: ReportPackId {ReportPackId}", input.ReportPackId);
            return Failure(startedAt);
        }
    }

    private static ReportBranchResult Success(ReportContent content, long startedAt) =>
        new("Recognition", true, content.ToJson(), null,
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    private static ReportBranchResult Failure(long startedAt) =>
        new("Recognition", false, null, "自我认识生成失败。",
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    private const string Instructions = """
        你负责回望指定日期内已经由用户原话支持的自我认识。
        只使用输入中的 recognitionRecords、metrics 和 evidence；不要使用常识补全，不要重新计算统计，不要调用工具。
        可以观察身份、特质、价值、偏好、习惯、能力、需求、目标和关系模式，但不能做人格测试或医学判断。
        一次情绪和一次事件不能被扩大为永久性格结论；只描述输入中已经成立的认识及其共同主题。
        每条 finding 必须引用输入中真实存在的 evidence ref；最多三条 finding、两个 reflectionQuestions。
        资料有限时把限制写进 uncertainties。

        只返回一个 JSON 对象，字段必须与下例完全一致，不要增加字段：
        {"headline":"对回应与陪伴的需要变得更清晰","summary":"两条记录都提到对朋友回应的在意，反映了这段时间被重视和被回应的需要。","findings":[{"title":"在意朋友是否回应","observation":"用户在不同原话中明确表达了对朋友回应的关注，可以作为当前关系需要的一项认识。","evidenceRefs":["E3","E5"],"confidence":"High"}],"reflectionQuestions":["什么样的回应会让你感到真正被重视？"],"uncertainties":["这些认识来自当前周期，之后可能继续变化。"]}
        """;
}
