using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Echora.Api.Agents;

/// <summary>把一天的情绪记录与 CBT 观察归纳成一句当日总结和最多三条关键洞察。</summary>
public sealed class DayDigestAgent(
    StructuredAgentRunner runner,
    ILogger<DayDigestAgent> logger)
{
    /// <summary>运行一次非流式结构化归纳；输入已由服务端排好序并裁剪。</summary>
    public async ValueTask<Result?> RunAsync(string contextJson, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await runner.RunAsync<Result>(
                "day_digest_agent",
                "把今天的情绪记录和自我观察归纳成一句总结和最多三条关键洞察。",
                Instructions,
                [new ChatMessage(ChatRole.User, contextJson)],
                [],
                cancellationToken,
                temperature: 0.4f);
            var result = JsonSerializer.Deserialize<Result>(raw, JsonOptions);
            if (result is null) throw new InvalidDataException("日摘要 JSON 为空。");
            result.Insights ??= [];
            logger.LogInformation(
                "Day digest received: NarrativeLength {NarrativeLength}, InsightCount {InsightCount}",
                result.Narrative?.Length ?? 0,
                result.Insights.Length);
            return result;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Day digest Agent failed.");
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    [Description("一天的当日总结和关键洞察。")]
    public sealed class Result
    {
        [Description("当日总结：一到两句口语化回看；今天没有内容可写时为空字符串。")]
        public string? Narrative { get; set; }

        [Description("今天最值得看的关键洞察，最多三条。")]
        public InsightItem[]? Insights { get; set; }
    }

    [Description("一条把同一件事的多次记录合并后的关键洞察。")]
    public sealed class InsightItem
    {
        [Description("发生的事情：客观一句话，不加评价。")]
        public string Situation { get; set; } = "";

        [Description("ta 怎么看这件事：只用 ta 明确说过的想法；没有时为空。")]
        public string? Appraisal { get; set; }

        [Description("这条洞察涉及的情绪，取自输入的 family·subtype，最多三个，按出现顺序。")]
        public string[]? Emotions { get; set; }

        [Description("后续进展：更晚的记录里 ta 怎么处理、说法或情绪强度有什么变化；没有更晚记录时为空。")]
        public string? FollowUp { get; set; }

        [Description("支撑这条洞察的原话，逐字摘自输入，一到两条。")]
        public string[]? Quotes { get; set; }

        [Description("这条洞察合并了哪些输入项的 observationId；输入里没有 CBT 观察时为空数组。")]
        public long[]? ObservationIds { get; set; }
    }

    private const string Instructions = """
        你在帮用户回看「今天」。输入是今天按时间排好的情绪记录（emotionRecords）和自我观察（cbtObservations）。
        只使用输入中的内容，不补常识，不重算统计，不调用工具，不诊断疾病，不评价用户的性格或人品。

        narrative（当日总结）：
        - 一到两句，最多 60 字，像朋友随口说的话，不用「你应该」「建议你」「要相信自己」这类口号和指导腔；
        - 必须落在今天真实记录到的具体事情上，不要只复述情绪名称或数量；
        - 结尾带一句今天能带走的东西：一个新的看法，或一个很小的下一步，必须由输入支持；
        - 只说事情本身，不要提到「记录」「输入」「今天没有更晚的记录」之类关于数据的话；
        - 输入里没有任何情绪记录和自我观察时，narrative 返回空字符串。

        insights（关键洞察）：最多三条，只挑今天最值得看的。
        - 同一件事的多次记录必须合并成一条，不要逐句复述，也不要为了凑数拆开；
        - situation：客观写发生了什么，一句话，不加评价；
        - appraisal：只写用户明确说过的想法或看法，不替 ta 归纳动机，不贴认知偏差标签，没有就留空；
        - emotions：从输入的 family 和 subtype 里选，写成「家族·子类」，最多三个，按出现顺序；
        - followUp：只能来自这条洞察最早原话之后、时间更晚的记录，写 ta 后来怎么处理、说法有没有变化、情绪强度有没有变化；
          没有更晚的记录时必须留空，禁止写「暂无进展」「还在继续」这类填充话；
        - quotes：逐字摘自输入的用户原话，一到两条，不改写不拼接；
        - observationIds：写出这条洞察合并了输入里哪些 observationId。

        严格禁止出现「反复」「一直」「总是」「模式」「循环」「习惯性」「每次」以及任何暗示长期倾向的说法，
        今天一天的记录不足以支撑这类判断。

        只返回一个 JSON 对象，字段必须与下例完全一致，不要增加字段：
        {"narrative":"周末被临时加派工作，那股憋闷压了一下午。后来你只写了三行交接说明就没那么堵了，先划清范围好像比先动手更管用。","insights":[{"situation":"下午被通知周末要赶工。","appraisal":"觉得自己的时间是最先被牺牲的那份。","emotions":["愤怒·烦躁","焦虑·压力"],"followUp":"晚上写了三行交接说明发出去，提到「至少现在知道该做什么了」，憋闷从 4 降到 2。","quotes":["又是周末赶工，凭什么都是我"],"observationIds":[12]}]}
        今天没有内容可写时返回：{"narrative":"","insights":[]}
        """;
}
