using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echora.Api.Plugins;
using Echora.Api.Services;
using Echora.Api.Workflows;
using Microsoft.Extensions.AI;

namespace Echora.Api.Agents;

/// <summary>从当前目标消息或一刻整理有原话依据的情绪。</summary>
public sealed class EmotionSubagent(
    StructuredAgentRunner runner,
    MemoryRetrievalService retrieval,
    ILogger<EmotionSubagent> logger)
{
    /// <summary>运行一次带完整查询 Tool 循环的非流式结构化分析。</summary>
    public async ValueTask<AnalysisBranchResult> RunAsync(AnalysisInput input, CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var query = new SelfRecordQueryPlugin(retrieval, input.UserId);
            var raw = await runner.RunAsync<Result>(
                "emotion_subagent",
                "整理当前消息或一刻中有原话依据的用户情绪。",
                Instructions,
                input.Messages,
                [AIFunctionFactory.Create(query.SearchAsync)],
                cancellationToken,
                temperature: 0.2f);
            var result = JsonSerializer.Deserialize<Result>(raw, JsonOptions)
                ?? throw new InvalidDataException("情绪 JSON 为空。");
            var discardedCount = Sanitize(result, input);
            logger.LogInformation(
                "Emotion result received: AnalysisRunId {AnalysisRunId}, RetrospectiveOnly {RetrospectiveOnly}, EmotionCount {EmotionCount}, HasCbtObservation {HasCbtObservation}",
                input.AnalysisRunId,
                result.RetrospectiveOnly,
                result.Emotions?.Length ?? 0,
                result.CbtObservation is not null);
            if (discardedCount > 0)
                logger.LogWarning(
                    "Emotion result discarded invalid items: AnalysisRunId {AnalysisRunId}, DiscardedCount {DiscardedCount}",
                    input.AnalysisRunId,
                    discardedCount);
            return new AnalysisBranchResult(
                "Emotion", true, JsonSerializer.Serialize(result, JsonOptions), null,
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Emotion Subagent failed: AnalysisRunId {AnalysisRunId}, TargetMessageId {TargetMessageId}, MomentId {MomentId}",
                input.AnalysisRunId,
                input.TargetMessageId,
                input.MomentId);
            return new AnalysisBranchResult(
                "Emotion", false, null,
                exception is JsonException or InvalidDataException ? exception.Message : "情绪模型调用失败。",
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    /// <summary>保留满足固定词表和最小落库契约的情绪。</summary>
    private static int Sanitize(Result result, AnalysisInput input)
    {
        result.Emotions ??= [];
        if (result.RetrospectiveOnly || input.IsMoment && !input.HasExplicitText)
        {
            var removed = result.Emotions.Length + (result.CbtObservation is null ? 0 : 1);
            result.Emotions = [];
            result.CbtObservation = null;
            return removed;
        }

        var valid = result.Emotions
            .Where(item => Families.TryGetValue(item.Family?.Trim() ?? "", out var subtypes)
                && subtypes.Contains(item.Subtype?.Trim() ?? "")
                && item.Intensity is >= 1 and <= 5
                && !string.IsNullOrWhiteSpace(item.Summary)
                && !string.IsNullOrWhiteSpace(item.EvidenceQuote)
                && input.ExplicitText.Contains(item.EvidenceQuote.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var discardedCount = result.Emotions.Length - valid.Length;
        result.Emotions = valid;
        if (result.CbtObservation is not null)
        {
            var item = result.CbtObservation;
            item.Situation = item.Situation?.Trim() ?? "";
            item.AutomaticThought = Clean(item.AutomaticThought);
            item.BodySensation = Clean(item.BodySensation);
            item.Behavior = Clean(item.Behavior);
            item.ImmediateOutcome = Clean(item.ImmediateOutcome);
            if (item.Situation.Length == 0
                || item.AutomaticThought is null
                    && item.BodySensation is null
                    && item.Behavior is null
                    && item.ImmediateOutcome is null)
            {
                result.CbtObservation = null;
                discardedCount++;
            }
        }
        return discardedCount;
    }

    /// <summary>把空白模型字段统一转换成空值。</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>固定 11 个情绪家族和允许的子类。</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Families =
        new Dictionary<string, IReadOnlySet<string>>
        {
            ["愉悦"] = new HashSet<string>(["开心", "快乐", "满足", "欣慰", "充实", "自豪", "释然", "兴奋", "愉快"]),
            ["平静"] = new HashSet<string>(["安心", "放松", "安宁", "平和", "稳定", "从容"]),
            ["期待"] = new HashSet<string>(["希望", "向往", "好奇", "兴趣", "憧憬", "迫不及待"]),
            ["亲近"] = new HashSet<string>(["喜爱", "爱", "感激", "感动", "思念", "信任", "依恋", "被理解"]),
            ["惊讶"] = new HashSet<string>(["意外", "吃惊", "震惊", "敬畏"]),
            ["低落"] = new HashSet<string>(["难过", "悲伤", "失望", "孤独", "无聊", "无力", "沮丧", "遗憾"]),
            ["焦虑"] = new HashSet<string>(["担忧", "紧张", "压力", "迷茫", "不安", "纠结", "烦忧"]),
            ["恐惧"] = new HashSet<string>(["害怕", "畏惧", "惊恐", "恐慌"]),
            ["愤怒"] = new HashSet<string>(["生气", "烦躁", "恼火", "愤慨", "挫败"]),
            ["厌恶"] = new HashSet<string>(["反感", "恶心", "排斥", "鄙视", "轻蔑"]),
            ["自责"] = new HashSet<string>(["内疚", "羞愧", "尴尬", "后悔", "自我否定"]),
        };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    [Description("当前目标消息或一刻中有原话依据的情绪。")]
    public sealed class Result
    {
        [Description("当前内容是否只回忆今天以前的感受。")]
        public bool RetrospectiveOnly { get; set; }

        [Description("当前目标新增的情绪。")]
        public EmotionItem[]? Emotions { get; set; }

        [Description("当前目标中由原话直接支持的一条情境、想法、身体反应、行为和结果；依据不足时为空。")]
        public CbtObservationItem? CbtObservation { get; set; }
    }

    [Description("当前时间点的一种情绪。")]
    public sealed class EmotionItem
    {
        [Description("固定情绪家族。")]
        public string Family { get; set; } = "";

        [Description("所选家族中的固定情绪子类。")]
        public string Subtype { get; set; } = "";

        [Description("本次表达强度：1 轻微，2 清楚但影响有限，3 当前主要感受，4 强烈，5 非常强烈。")]
        public short Intensity { get; set; }

        [Description("情绪和当前直接语境的简短中文说明。")]
        public string Summary { get; set; } = "";

        [Description("当前目标消息中能直接证明该情绪的一段连续原话；没有原话依据时不要输出该情绪。")]
        public string EvidenceQuote { get; set; } = "";
    }

    [Description("当前目标中一条有原话依据的 CBT 自我观察。")]
    public sealed class CbtObservationItem
    {
        [Description("客观发生的情境，不加入评价。")]
        public string Situation { get; set; } = "";

        [Description("用户明确说出的即时想法；没有时为空。")]
        public string? AutomaticThought { get; set; }

        [Description("用户明确说出的身体感受；没有时为空。")]
        public string? BodySensation { get; set; }

        [Description("用户明确说出的做法或回避；没有时为空。")]
        public string? Behavior { get; set; }

        [Description("用户明确说出的直接结果；没有时为空。")]
        public string? ImmediateOutcome { get; set; }
    }

    private const string Instructions = """
        聊天输入只分析带【本次唯一分析目标】的 User Message；带【历史上下文，不生成记录】的消息和查询结果只帮助理解，不得重复产出情绪。

        必须先调用一次 search_self_records，批量查询当前内容可能涉及的既有认识和情绪。
        只有当前用户原话能作为依据；助手消息、Tool Result、照片人物表情和环境不能单独证明用户情绪。
        一刻没有用户文字描述时 emotions 必须返回 []。
        没有明确措辞或清晰上下文依据时返回空列表；没有情绪不等于平静。
        每条情绪的 evidenceQuote 必须逐字摘自当前目标消息；无法摘出连续原话时不要输出该情绪。
        提问、请求和回忆查询不是情绪；除非用户同时明确表达当前感受，否则 emotions 返回 []。
        同一时间可以有多种情绪，每种分别输出。原则、计划、观点和事实本身不是情绪。
        family 和 subtype 必须从固定词表选择：
        愉悦=开心、快乐、满足、欣慰、充实、自豪、释然、兴奋、愉快；平静=安心、放松、安宁、平和、稳定、从容；
        期待=希望、向往、好奇、兴趣、憧憬、迫不及待；亲近=喜爱、爱、感激、感动、思念、信任、依恋、被理解；
        惊讶=意外、吃惊、震惊、敬畏；低落=难过、悲伤、失望、孤独、无聊、无力、沮丧、遗憾；
        焦虑=担忧、紧张、压力、迷茫、不安、纠结、烦忧；恐惧=害怕、畏惧、惊恐、恐慌；
        愤怒=生气、烦躁、恼火、愤慨、挫败；厌恶=反感、恶心、排斥、鄙视、轻蔑；
        自责=内疚、羞愧、尴尬、后悔、自我否定。
        强度只按当前表达明显程度填写 1 到 5，拿不准选较低等级。
        当前内容只回忆昨天或更早经历时 retrospectiveOnly=true，不生成情绪。
        历史 Tool Result 只能用于匹配，不能成为当前情绪的事实依据。

        同一次输出最多整理一条 CBT 自我观察：
        - situation 只写当前原话中客观发生的情境，不加入评价；
        - automaticThought 只写用户明确说出的即时想法，不能猜测；
        - bodySensation 只写用户明确说出的身体感觉；
        - behavior 只写用户明确做了、没有做或回避了什么；
        - immediateOutcome 只写用户明确说出的直接结果。
        不输出认知偏差标签、替代想法、诊断或治疗建议。情境不明确，或除情境外没有任何明确字段时，cbtObservation 返回 null。

        只返回一个 JSON 对象，字段必须与下例完全一致：
        {"retrospectiveOnly":false,"emotions":[{"family":"焦虑","subtype":"担忧","intensity":3,"summary":"想到明天的面试时感到担忧。","evidenceQuote":"我很担心明天的面试"}],"cbtObservation":{"situation":"明天需要参加面试。","automaticThought":"我肯定会表现得很差。","bodySensation":null,"behavior":"一直拖延准备。","immediateOutcome":"准备时间进一步减少。"}}
        没有情绪或 CBT 观察时也要保留完整字段：
        {"retrospectiveOnly":false,"emotions":[],"cbtObservation":null}
        """;
}
