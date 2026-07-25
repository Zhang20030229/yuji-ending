using System.Text.Json;
using Echora.Api.Services;
using Echora.Api.Workflows;
using Microsoft.Extensions.AI;

namespace Echora.Api.Agents;

/// <summary>只分析指定日期内情绪记录的报告子智能体。</summary>
public sealed class EmotionReportAgent(
    StructuredAgentRunner runner,
    ILogger<EmotionReportAgent> logger)
{
    /// <summary>根据固定情绪记录和指标生成一份带依据的情绪脉络。</summary>
    public async ValueTask<ReportBranchResult> RunAsync(
        HeartReportInput input,
        CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var section = input.Sections.Single(item => item.Kind == "Emotion");
            var raw = await runner.RunAsync<ReportContent>(
                "emotion_report_agent",
                "根据给定情绪记录、统计和原话依据生成情绪脉络。",
                Instructions,
                [new ChatMessage(ChatRole.User, section.ContextJson)],
                [],
                cancellationToken);
            var content = ReportContent.Parse(
                raw,
                section.AllowedEvidenceRefs.ToHashSet(StringComparer.Ordinal),
                input.UserDisplayName,
                EvidenceDates(section));
            return Success(content, startedAt);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Emotion report Agent failed: ReportPackId {ReportPackId}", input.ReportPackId);
            return Failure(startedAt);
        }
    }

    private static ReportBranchResult Success(ReportContent content, long startedAt) =>
        new("Emotion", true, content.ToJson(), null,
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    private static ReportBranchResult Failure(long startedAt) =>
        new("Emotion", false, null, "情绪脉络生成失败。",
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    /// <summary>构建服务端跨日期校验使用的报告内临时引用表。</summary>
    private static IReadOnlyDictionary<string, DateOnly> EvidenceDates(ReportSectionInput section) =>
        (JsonSerializer.Deserialize<ReportEvidence[]>(section.EvidenceJson, JsonOptions) ?? [])
            .GroupBy(item => item.Ref, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(group.First().OccurredAt, ShanghaiTimeZone).DateTime),
                StringComparer.Ordinal);

    private const string Instructions = """
        你负责回望指定日期内用户明确表达的情绪。
        只使用输入中的 emotionRecords、metrics 和 evidence；不要使用常识补全，不要重新计算统计，不要调用工具。
        cbtObservations 是从原话整理出的情境、自动想法、身体感受、行为和直接结果，只能使用其中明确存在的字段。
        区分情绪分布、时间变化和可能共同出现的生活语境。只能写“同时出现、可能有关”，不能写未经证明的因果。
        不诊断疾病，不建立正负情绪总分，不把没有记录的日期解释为平静。
        每条 finding 必须引用输入中真实存在的 evidence ref；最多三条 finding、两个 reflectionQuestions。
        cbtCycles 最多两条。每条必须由两个或更多不同日期的 evidence ref 支持；只有一次观察时不得称为反复、模式或循环。
        helpfulResponses 最多两条。只有原话明确表示“做了某个行为后，情绪、行动或处境有所缓解或改善”时才能记录；
        单纯回忆、提问、搜索记录、表达愿望或尚未验证的建议都不算有帮助应对。不得承诺未来效果。
        smallExperiment 最多一个，只能基于已有循环或有帮助应对，必须具体、低风险、可执行，不是治疗方案。
        资料有限时把限制写进 uncertainties。

        只返回一个 JSON 对象，字段必须与下例完全一致，不要增加字段：
        {"headline":"面试压力下出现了担忧与拖延的循环","summary":"两次不同日期的记录都显示，面试相关担忧和拖延准备在相似情境中一起出现。","findings":[{"title":"担忧与拖延在两次面试准备中同时出现","observation":"两次记录都出现了对表现不佳的担忧，随后减少或推迟准备。","evidenceRefs":["E2","E7"],"confidence":"High"}],"reflectionQuestions":["下一次出现类似担忧时，什么最小行动能帮助你开始准备？"],"uncertainties":["现有记录只能说明这些内容在两次情境中同时出现，不能证明因果。"],"cbtCycles":[{"title":"担忧—拖延—准备时间减少","observation":"在两次不同日期的面试准备中，担忧表现不佳后都出现了拖延，直接结果都是可用准备时间减少。","evidenceRefs":["E2","E7"]}],"helpfulResponses":[{"observation":"另一次记录中，先完成十分钟准备后焦虑有所缓解。","evidenceRefs":["E9"]}],"smallExperiment":{"title":"先做十分钟准备","action":"下一次出现同类担忧时，只安排十分钟完成一个最小准备步骤。","reflectionQuestion":"十分钟后，担忧和继续行动的难度有什么变化？"}}
        没有足够 CBT 资料时仍要返回完整字段：cbtCycles=[]、helpfulResponses=[]、smallExperiment=null。
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeZoneInfo ShanghaiTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
}
