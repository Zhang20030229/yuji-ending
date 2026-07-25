using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echora.Api.Plugins;
using Echora.Api.Workflows;
using Microsoft.Extensions.AI;
using SqlSugar;

namespace Echora.Api.Agents;

/// <summary>从当前目标消息或一刻整理有原话依据的用户认识。</summary>
public sealed class RecognitionSubagent(
    StructuredAgentRunner runner,
    ISqlSugarClient db,
    ILogger<RecognitionSubagent> logger)
{
    /// <summary>运行一次带完整查询 Tool 循环的非流式结构化分析。</summary>
    public async ValueTask<AnalysisBranchResult> RunAsync(AnalysisInput input, CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var query = new SelfRecordQueryPlugin(db, input.UserId);
            var raw = await runner.RunAsync<Result>(
                "recognition_subagent",
                "整理当前消息或一刻中有原话依据的用户认识。",
                Instructions,
                input.Messages,
                [AIFunctionFactory.Create(query.SearchAsync)],
                cancellationToken);
            var result = JsonSerializer.Deserialize<Result>(raw, JsonOptions)
                ?? throw new InvalidDataException("认识 JSON 为空。");
            var discardedCount = Sanitize(result, input);
            logger.LogInformation(
                "Recognition result received: AnalysisRunId {AnalysisRunId}, RetrospectiveOnly {RetrospectiveOnly}, RecognitionCount {RecognitionCount}",
                input.AnalysisRunId,
                result.RetrospectiveOnly,
                result.Recognitions?.Length ?? 0);
            if (discardedCount > 0)
                logger.LogWarning(
                    "Recognition result discarded invalid items: AnalysisRunId {AnalysisRunId}, DiscardedCount {DiscardedCount}",
                    input.AnalysisRunId,
                    discardedCount);
            return new AnalysisBranchResult(
                "Recognition", true, JsonSerializer.Serialize(result, JsonOptions), null,
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Recognition Subagent failed: AnalysisRunId {AnalysisRunId}, TargetMessageId {TargetMessageId}, MomentId {MomentId}",
                input.AnalysisRunId,
                input.TargetMessageId,
                input.MomentId);
            return new AnalysisBranchResult(
                "Recognition", false, null,
                exception is JsonException or InvalidDataException ? exception.Message : "认识模型调用失败。",
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    /// <summary>保留满足最小落库契约的认识；单条坏数据不能拖垮整个后台分支。</summary>
    private static int Sanitize(Result result, AnalysisInput input)
    {
        result.Recognitions ??= [];
        if (result.RetrospectiveOnly || input.IsMoment && !input.HasExplicitText)
        {
            var removed = result.Recognitions.Length;
            result.Recognitions = [];
            return removed;
        }

        var categories = new HashSet<string>(
            ["Identity", "Trait", "Value", "Preference", "Habit", "Ability", "Need", "Goal", "Relationship"],
            StringComparer.OrdinalIgnoreCase);
        var valid = new List<RecognitionItem>(result.Recognitions.Length);
        foreach (var item in result.Recognitions)
        {
            item.Category = categories.FirstOrDefault(value => value.Equals(item.Category?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
            item.Content = item.Content?.Trim() ?? "";
            item.Keywords = item.Keywords.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().Take(5).ToArray();
            if (item.Category.Length > 0 && item.Content.Length > 0)
                valid.Add(item);
        }

        var discardedCount = result.Recognitions.Length - valid.Count;
        result.Recognitions = valid.ToArray();
        return discardedCount;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    [Description("当前目标消息或一刻中有原话依据的用户认识。")]
    public sealed class Result
    {
        [Description("当前内容是否只回忆今天以前的经历。")]
        public bool RetrospectiveOnly { get; set; }

        [Description("当前目标新增的用户认识。")]
        public RecognitionItem[]? Recognitions { get; set; }
    }

    [Description("一条有当前用户原话依据的认识。")]
    public sealed class RecognitionItem
    {
        [Description("分类：Identity、Trait、Value、Preference、Habit、Ability、Need、Goal 或 Relationship。")]
        public string Category { get; set; } = "";

        [Description("简短、具体、可由当前原话直接解释的中文认识。")]
        public string Content { get; set; } = "";

        [Description("用于搜索的 1 到 5 个简短中文词组。")]
        public string[] Keywords { get; set; } = [];
    }

    private const string Instructions = """
        聊天输入只分析带【本次唯一分析目标】的 User Message；带【历史上下文，不生成记录】的消息和查询结果只帮助理解，不得重复产出认识。

        必须先调用一次 search_self_records，批量查询当前内容可能涉及的既有认识和情绪。
        只有当前用户原话能作为依据；助手消息、Tool Result、照片外观和设备位置不能单独证明用户特征。
        一刻没有用户文字描述时 recognitions 必须返回 []。
        没有充分依据时返回空列表，不为产生记录而推断，不做心理或医学诊断。
        提问、请求和回忆查询不是用户认识，不得把查询结果重新输出。
        提问、知识问答、一次具体事件或一次当前情绪通常不能证明长期特征。
        一次自动想法、担忧、负面预测或自我评价不等于稳定认识。
        “这次肯定失败”“我现在很没用”等内容属于当下想法；除非用户明确表示这是长期、反复且稳定的自我认识，否则不要输出。
        用户明确表达的身份阶段、性格倾向、原则、偏好、习惯、能力、需求、目标或关系模式可以分别记录。
        一条认识只表达一个分类；关键词填写 1 到 5 个便于搜索的简短词组。
        当前内容只回忆昨天或更早经历时 retrospectiveOnly=true，不生成认识。
        历史 Tool Result 只能用于匹配和避免重复，不能成为当前认识的事实依据。

        只返回一个 JSON 对象，字段必须与下例完全一致；没有认识时 recognitions 返回 []：
        {"retrospectiveOnly":false,"recognitions":[{"category":"Preference","content":"喜欢吃炸鸡。","keywords":["喜欢的食物","炸鸡"]}]}
        """;
}
