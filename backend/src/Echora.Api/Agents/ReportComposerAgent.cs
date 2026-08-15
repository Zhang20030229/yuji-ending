using System.Text.Json;
using Echora.Api.Workflows;
using Microsoft.Extensions.AI;

namespace Echora.Api.Agents;

/// <summary>只汇总成功子报告的综合心迹智能体。</summary>
public sealed class ReportComposerAgent(
    StructuredAgentRunner runner,
    ILogger<ReportComposerAgent> logger)
{
    /// <summary>汇总至少两个成功分区，不读取原聊天，也不新增事实。</summary>
    public async ValueTask<ReportBranchResult> RunAsync(
        HeartReportInput input,
        IReadOnlyList<ReportBranchResult> sections,
        CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var successful = sections.Where(item => item.Succeeded && item.ContentJson is not null).ToArray();
            var minimum = ReportPeriod.MinimumComposableSections(input.PeriodType);
            if (successful.Length < minimum)
                throw new InvalidOperationException($"综合心迹至少需要 {minimum} 个成功子报告。");
            var metrics = input.Sections
                .Where(section => successful.Any(result => result.Kind == section.Kind))
                .ToDictionary(section => section.Kind, section => JsonSerializer.Deserialize<JsonElement>(section.MetricsJson));
            var context = JsonSerializer.Serialize(new
            {
                userDisplayName = input.UserDisplayName,
                period = new { startDate = input.StartDate, endDate = input.EndDate },
                sections = successful.Select(item => new
                {
                    kind = item.Kind,
                    content = JsonSerializer.Deserialize<JsonElement>(item.ContentJson!),
                }),
                metrics,
                latestWellbeing = string.IsNullOrWhiteSpace(input.LatestWellbeingJson)
                    ? (JsonElement?)null
                    : JsonSerializer.Deserialize<JsonElement>(input.LatestWellbeingJson),
            });
            var allowedRefs = successful
                .SelectMany(result => input.Sections.Single(section => section.Kind == result.Kind).AllowedEvidenceRefs)
                .ToHashSet(StringComparer.Ordinal);
            var raw = await runner.RunAsync<ReportContent>(
                "report_composer_agent",
                "只汇总已经完成的子报告，生成综合心迹。",
                ReportPeriod.IsDaily(input.PeriodType)
                    ? DailyInstructions
                    : Instructions,
                [new ChatMessage(ChatRole.User, context)],
                [],
                cancellationToken);
            var content = ReportContent.Parse(
                raw,
                allowedRefs,
                input.UserDisplayName,
                null,
                ReportPeriod.IsDaily(input.PeriodType));
            var emotion = successful.FirstOrDefault(item => item.Kind == "Emotion" && item.ContentJson is not null);
            if (emotion is not null)
            {
                // 综合智能体不得创造新的 CBT 结论；最终值只取已经通过情绪报告校验的内容。
                var emotionContent = JsonSerializer.Deserialize<ReportContent>(emotion.ContentJson!, JsonOptions);
                content.CbtCycles = emotionContent?.CbtCycles ?? [];
                content.HelpfulResponses = emotionContent?.HelpfulResponses ?? [];
                content.SmallExperiment = emotionContent?.SmallExperiment;
            }
            else
            {
                content.CbtCycles = [];
                content.HelpfulResponses = [];
                content.SmallExperiment = null;
            }
            return new ReportBranchResult(
                "Overall",
                true,
                content.ToJson(),
                null,
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Report composer Agent failed: ReportPackId {ReportPackId}", input.ReportPackId);
            return new ReportBranchResult(
                "Overall",
                false,
                null,
                "综合心迹生成失败。",
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private const string Instructions = """
        你负责把已经完成的两份或更多子报告汇总成一份综合心迹。
        只能使用输入中的 sections、metrics 和可选 latestWellbeing；不要读取或猜测原聊天，不要调用工具。
        不得新增子报告没有出现的人物、事件、情绪、性格或因果关系。
        finding 的 evidenceRefs 只能原样复用子报告已经引用的编号。
        WHO-5 分数由固定规则算好，你只能解释，不能重新计分、与其他指标相加或给出疾病诊断。
        寻找生活、情绪、人际和认识之间被现有子报告共同支持的联系；没有联系时分别概括即可。
        cbtCycles、helpfulResponses 和 smallExperiment 只能原样复用 Emotion 子报告已经给出的内容；Emotion 没有时必须分别返回 []、[] 和 null。
        最多三条 finding、两个 reflectionQuestions；资料限制写进 uncertainties。

        只返回一个 JSON 对象，字段必须与下例完全一致，不要增加字段：
        {"headline":"陪伴与被回应是这段时间反复出现的主题","summary":"生活、人际和认识回望共同显示，朋友相处与回应在这段时间占据了重要位置。","findings":[{"title":"陪伴同时出现在生活与情绪记录中","observation":"生活回望记录了与朋友见面，情绪脉络也在相同依据中出现愉悦；这表示二者在记录中同时出现，不代表已经证明因果。","evidenceRefs":["E1","E4"],"confidence":"High"}],"reflectionQuestions":["什么样的陪伴最符合你现在的需要？"],"uncertainties":["报告只反映本周期留下的记录。"],"cbtCycles":[],"helpfulResponses":[],"smallExperiment":null}
        """;

    private const string DailyInstructions = """
        你负责把今天已经完成的一份或多份子报告汇总成一份今日心迹。
        只能使用输入中的 sections、metrics 和可选 latestWellbeing；不要读取或猜测原聊天，不要调用工具。
        不得新增子报告没有出现的人物、事件、情绪、性格或因果关系。
        finding 的 evidenceRefs 只能原样复用子报告已经引用的编号。
        WHO-5 分数由固定规则算好，你只能解释，不能重新计分、与其他指标相加或给出疾病诊断。
        只有一份子报告时不要强行编造跨领域联系，如实概括这一个方面即可。
        cbtCycles、helpfulResponses 和 smallExperiment 只能原样复用 Emotion 子报告已经给出的内容；Emotion 没有时必须分别返回 []、[] 和 null。
        最多三条 finding、两个 reflectionQuestions；资料限制写进 uncertainties。

        本次只覆盖今天一天，而且今天可能还没结束。
        所有表述必须限定在今天，禁止出现「这段时间」「这一周」「反复」「一直」「总是」「模式」「习惯性」等跨日或长期措辞。
        没有记录的时段一律不解释为平静、稳定或好转，也不由今天一次记录推断性格、能力或长期状态。

        只返回一个 JSON 对象，字段必须与下例完全一致，不要增加字段：
        {"headline":"今天的重心落在被临时加派的工作上","summary":"今天的情绪记录集中在被要求周末赶工之后的憋闷，暂时没有其他方面的记录。","findings":[{"title":"今天的憋闷与加班要求同时出现","observation":"今天的记录显示，被通知周末赶工之后出现了强度 4 的憋闷。这是今天的一次记录，不代表已经证明因果。","evidenceRefs":["E1"],"confidence":"Medium"}],"reflectionQuestions":["今天这份憋闷里，最想被听见的是哪一句？"],"uncertainties":["今天可能还没结束，这份回顾只覆盖到目前为止的记录。"],"cbtCycles":[],"helpfulResponses":[],"smallExperiment":null}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
