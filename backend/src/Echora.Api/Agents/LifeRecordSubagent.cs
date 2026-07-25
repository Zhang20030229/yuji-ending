using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echora.Api.Plugins;
using Echora.Api.Workflows;
using Microsoft.Extensions.AI;
using SqlSugar;

namespace Echora.Api.Agents;

/// <summary>从当前目标消息或一刻整理生活记录。</summary>
public sealed class LifeRecordSubagent(
    StructuredAgentRunner runner,
    ISqlSugarClient db,
    ILogger<LifeRecordSubagent> logger)
{
    /// <summary>运行一次带完整查询 Tool 循环的非流式结构化分析。</summary>
    public async ValueTask<AnalysisBranchResult> RunAsync(AnalysisInput input, CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var query = new LifeRecordQueryPlugin(db, input.UserId);
            var raw = await runner.RunAsync<Result>(
                "life_record_subagent",
                "整理当前消息或一刻中的片段、人物、地点和事件。",
                input.IsMoment
                    ? $"{BuildInstructions(input)}\n本次来源是一刻：fragment 的 title 和 summary 必须都是空字符串。"
                    : $"{BuildInstructions(input)}\n本次来源是聊天消息：fragment 必须包含非空 title 和 summary。",
                input.Messages,
                [AIFunctionFactory.Create(query.SearchAsync)],
                cancellationToken);
            var result = JsonSerializer.Deserialize<Result>(raw, JsonOptions)
                ?? throw new InvalidDataException("生活记录 JSON 为空。");
            Validate(result, input);
            logger.LogInformation(
                "LifeRecord result received: AnalysisRunId {AnalysisRunId}, RetrospectiveOnly {RetrospectiveOnly}, HasFragment {HasFragment}, FragmentTitleLength {FragmentTitleLength}, FragmentSummaryLength {FragmentSummaryLength}, PeopleCount {PeopleCount}, PlaceCount {PlaceCount}, EventCount {EventCount}, UnresolvedCount {UnresolvedCount}",
                input.AnalysisRunId,
                result.RetrospectiveOnly,
                result.Fragment is not null,
                result.Fragment?.Title?.Length ?? 0,
                result.Fragment?.Summary?.Length ?? 0,
                result.People?.Length ?? 0,
                result.Places?.Length ?? 0,
                result.Events?.Length ?? 0,
                result.UnresolvedMentions?.Length ?? 0);
            return Success("LifeRecord", result, startedAt);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "LifeRecord Subagent failed: AnalysisRunId {AnalysisRunId}, TargetMessageId {TargetMessageId}, MomentId {MomentId}",
                input.AnalysisRunId,
                input.TargetMessageId,
                input.MomentId);
            return Failure("LifeRecord", exception, startedAt);
        }
    }

    /// <summary>只校验固定数据契约，不二次判断模型语义。</summary>
    private static void Validate(Result result, AnalysisInput input)
    {
        result.People ??= [];
        result.Places ??= [];
        result.Events ??= [];
        result.AttachmentDescriptions ??= [];
        result.UnresolvedMentions ??= [];
        if (input.IsMoment && !input.HasExplicitText)
        {
            result.People = [];
            result.Places = [];
            result.Events = [];
            result.AttachmentDescriptions = [];
            result.UnresolvedMentions = [];
        }
        if (result.RetrospectiveOnly)
        {
            result.People = [];
            result.Places = [];
            result.Events = [];
            result.UnresolvedMentions = [];
        }
        var relationships = new HashSet<string>(["Family", "Partner", "Friend", "Classmate", "Colleague", "Acquaintance", "Other", "Unknown"]);
        result.People = result.People
            .Where(item => !string.IsNullOrWhiteSpace(item.Name)
                && !item.Name.Trim().Equals(input.UserDisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var item in result.People)
        {
            if (!relationships.Contains(item.Relationship)) item.Relationship = "Unknown";
            item.Summary = NormalizePersistentSummary(
                item.Summary,
                input,
                $"{input.UserDisplayName}提到了{item.Name}。");
            item.ImageIndexes = SanitizeImageIndexes(item.ImageIndexes, input.AttachmentIds.Length);
        }
        result.Places = result.Places.Where(item => !string.IsNullOrWhiteSpace(item.Name)).ToArray();
        foreach (var item in result.Places)
        {
            item.Summary = NormalizePersistentSummary(
                item.Summary,
                input,
                $"{input.UserDisplayName}提到了{item.Name}。");
            item.ImageIndexes = SanitizeImageIndexes(item.ImageIndexes, input.AttachmentIds.Length);
        }
        result.Events = result.Events
            .Where(item => !string.IsNullOrWhiteSpace(item.Title) && !string.IsNullOrWhiteSpace(item.Summary))
            .ToArray();
        foreach (var item in result.Events)
        {
            item.Summary = NormalizePersistentSummary(item.Summary, input, item.Title);
            item.PersonNames = item.PersonNames
                .Where(name => !name.Trim().Equals(input.UserDisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            item.ImageIndexes = SanitizeImageIndexes(item.ImageIndexes, input.AttachmentIds.Length);
        }
        if (!input.IsMoment
            && !string.IsNullOrWhiteSpace(result.Fragment.Title)
            && !string.IsNullOrWhiteSpace(result.Fragment.Summary))
            result.Fragment.Summary = NormalizePersistentSummary(
                result.Fragment.Summary,
                input,
                result.Fragment.Title);
        else if (!input.IsMoment)
            result.Fragment = new FragmentItem();
        result.UnresolvedMentions = result.UnresolvedMentions
            .Select(item =>
            {
                item.Kind = item.Kind?.Trim().ToLowerInvariant() ?? "";
                return item;
            })
            .Where(item => item.Kind is "person" or "place" && !string.IsNullOrWhiteSpace(item.Mention))
            .ToArray();
        foreach (var item in result.UnresolvedMentions)
            item.ImageIndexes = SanitizeImageIndexes(item.ImageIndexes, input.AttachmentIds.Length);
        result.AttachmentDescriptions = result.AttachmentDescriptions
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Take(input.AttachmentIds.Length)
            .ToArray();
    }

    /// <summary>只保留当前消息实际存在、从 1 开始的图片序号。</summary>
    private static int[] SanitizeImageIndexes(IEnumerable<int>? indexes, int imageCount) =>
        (indexes ?? [])
            .Where(index => index > 0 && index <= imageCount)
            .Distinct()
            .ToArray();

    /// <summary>
    /// 对模型已经写好的长期总结只补齐绝对日期和真实姓名。
    /// 语义仍由模型负责，这里不重写事实，避免偶发漏指令把“今天”或“用户”保存进长期记录。
    /// </summary>
    private static string NormalizePersistentSummary(
        string? summary,
        AnalysisInput input,
        string fallback)
    {
        var sourceTime = TimeZoneInfo.ConvertTime(input.OccurredAt, ShanghaiTimeZone);
        var prefix = $"{sourceTime:yyyy年M月d日}，";
        var displayName = input.UserDisplayName.Trim();
        var text = string.IsNullOrWhiteSpace(summary) ? fallback.Trim() : summary.Trim();
        text = text
            .Replace("用户", displayName, StringComparison.Ordinal)
            .Replace("当事人", displayName, StringComparison.Ordinal);
        foreach (var relativePrefix in new[] { "今天，", "今天", "昨天，", "昨天", "刚才，", "刚才" })
        {
            if (!text.StartsWith(relativePrefix, StringComparison.Ordinal)) continue;
            text = text[relativePrefix.Length..].TrimStart();
            break;
        }
        if (text.StartsWith(prefix, StringComparison.Ordinal))
        {
            if (text.Contains(displayName, StringComparison.Ordinal)) return text;
            text = text[prefix.Length..].TrimStart();
        }
        return $"{prefix}{(text.Contains(displayName, StringComparison.Ordinal) ? "" : $"{displayName}提到：")}{text}";
    }

    private static AnalysisBranchResult Success(string branch, Result result, long startedAt) =>
        new(branch, true, JsonSerializer.Serialize(result, JsonOptions), null,
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    private static AnalysisBranchResult Failure(string branch, Exception exception, long startedAt) =>
        new(branch, false, null,
            exception is JsonException or InvalidDataException ? exception.Message : "生活记录模型调用失败。",
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    [Description("当前目标消息或一刻的生活记录整理结果。")]
    public sealed class Result
    {
        [Description("当前内容是否只回忆本次来源日期之前的经历。")]
        public bool RetrospectiveOnly { get; set; }

        [Description("聊天片段的标题和总结；分析一刻时两个字段都为空字符串。")]
        public FragmentItem Fragment { get; set; } = new();

        [Description("当前内容明确涉及的人物。")]
        public PersonItem[]? People { get; set; }

        [Description("当前内容明确涉及的地点。")]
        public PlaceItem[]? Places { get; set; }

        [Description("当前内容中在本次来源时间已经发生或正在发生的事件。")]
        public EventItem[]? Events { get; set; }

        [Description("按输入图片顺序排列的客观图片说明。")]
        public string[]? AttachmentDescriptions { get; set; }

        [Description("仍不能确定的人物或地点称呼。")]
        public UnresolvedItem[]? UnresolvedMentions { get; set; }
    }

    [Description("一次聊天的温和摘要。")]
    public sealed class FragmentItem
    {
        [Description("卡片短标题。")]
        public string Title { get; set; } = "";

        [Description("结合已有片段与当前消息更新后的简短总结。")]
        public string Summary { get; set; } = "";
    }

    [Description("当前内容明确提到的人物。")]
    public sealed class PersonItem
    {
        [Description("用户使用的人物名称。")]
        public string Name { get; set; } = "";

        [Description("用户明确说明属于同一人的其他称呼。")]
        public string[] Aliases { get; set; } = [];

        [Description("与用户的关系：Family、Partner、Friend、Classmate、Colleague、Acquaintance、Other 或 Unknown。")]
        public string Relationship { get; set; } = "Unknown";

        [Description("更具体的关系词，例如大学同桌。")]
        public string[] RelationshipKeywords { get; set; } = [];

        [Description("当前目标对该人物新增的简短记录。")]
        public string Summary { get; set; } = "";

        [Description("与该人物直接相关的当前消息图片序号，从 1 开始；纯风景照不得填写。")]
        public int[] ImageIndexes { get; set; } = [];
    }

    [Description("当前内容明确提到的地点。")]
    public sealed class PlaceItem
    {
        [Description("用户使用的地点名称。")]
        public string Name { get; set; } = "";

        [Description("用户明确说明属于同一地点的其他称呼。")]
        public string[] Aliases { get; set; } = [];

        [Description("用户输入或设备位置明确提供的省。")]
        public string? Province { get; set; }

        [Description("用户输入或设备位置明确提供的市。")]
        public string? City { get; set; }

        [Description("当前目标对该地点新增的简短记录。")]
        public string Summary { get; set; } = "";

        [Description("能直接呈现该地点的当前消息图片序号，从 1 开始；人物背景中的地点也可以填写。")]
        public int[] ImageIndexes { get; set; } = [];
    }

    [Description("当前内容中在本次来源时间已经发生或正在发生的具体事件。")]
    public sealed class EventItem
    {
        [Description("事件卡片短标题。")]
        public string Title { get; set; } = "";

        [Description("忠于当前目标的事件总结。")]
        public string Summary { get; set; } = "";

        [Description("事件涉及的人物名称或已知别名。")]
        public string[] PersonNames { get; set; } = [];

        [Description("事件涉及的地点名称或已知别名。")]
        public string[] PlaceNames { get; set; } = [];

        [Description("能直接支持该事件的当前消息图片序号，从 1 开始。")]
        public int[] ImageIndexes { get; set; } = [];
    }

    [Description("需要用户稍后处理的人物或地点称呼。")]
    public sealed class UnresolvedItem
    {
        [Description("类型：person 或 place。")]
        public string Kind { get; set; } = "";

        [Description("当前目标中的原始称呼。")]
        public string Mention { get; set; } = "";

        [Description("为什么不能确定。")]
        public string Reason { get; set; } = "";

        [Description("与该待确认称呼直接相关的当前消息图片序号，从 1 开始。")]
        public int[] ImageIndexes { get; set; } = [];
    }

    /// <summary>使用真实姓名和绝对时间构造持久记录口径，避免“今天”和“用户”污染长期内容。</summary>
    private static string BuildInstructions(AnalysisInput input)
    {
        var sourceTime = TimeZoneInfo.ConvertTime(input.OccurredAt, ShanghaiTimeZone);
        var date = sourceTime.ToString("yyyy年M月d日");
        var displayName = input.UserDisplayName.Replace("\"", string.Empty).Trim();
        var aiName = input.AiName.Replace("\"", string.Empty).Trim();
        return $$"""
        聊天输入只整理带【本次唯一分析目标】的 User Message；带【历史上下文，不生成记录】的消息和查询结果只帮助理解，不得重复产出记录。

        记录对象姓名是“{{displayName}}”，对话中的 Assistant 名叫“{{aiName}}”。不要把 Assistant 建成人物卡。
        本次来源的上海时间是 {{sourceTime:yyyy-MM-dd HH:mm:ss}}。
        fragment、people、places、events 中的 summary 都是长期保存的第三人称事实记录：
        - 使用“{{date}}，……”开头，不使用“今天、昨天、刚才”等相对时间。
        - 必须使用“{{displayName}}”的真实姓名，不使用“用户、当事人、我、我们、你”等代称。
        - 像记录者一样客观、简洁地写清人物、地点和事情，不代入角色，不写对话式安慰或评价。

        必须先调用一次 search_life_records，批量查询当前内容可能涉及的人物、地点、事件和经历。
        聊天来源要用已有片段和当前消息更新 fragment；一刻来源不保存片段，fragment 两个字段留空。
        people、places 只输出当前目标明确涉及的人物或地点记录；即使查询结果里已经存在同名卡片，也要输出当前这次新经历，服务端会把它追加到已有卡片。纯回忆查询和历史上下文不能重复输出。
        只记录用户明确提到或结合上下文能唯一确定的人物与地点，不把用户本人建成人物卡。
        “这里、那里、这个地方”等指代没有唯一专名时必须进入地点待确认；即使图片只能看出“公园、咖啡馆”等场景类型，也不能把场景类型建成地点卡。
        别名只能来自用户明确说明，不能按相似度猜测。
        只有当前生活事实的落库因“她、他、那里”等受阻时才写入 unresolvedMentions；纯回忆查询不生成待确认。
        图片里未被用户提及的陌生面孔不要建卡或待确认。
        当前消息的图片按【图片1】、【图片2】顺序编号。每个人物、地点、事件和待确认项只填写与其直接相关的 imageIndexes。
        人物图片必须能看到该人物，且当前原话或上下文已经明确其身份；纯景点、风景、食物和物品照片不得关联到人物。
        同一张合照可以关联多个人物；地点允许使用人物照片中的背景；不要为了有封面而强行填写图片序号。
        一刻没有用户文字描述时，people、places、events、attachmentDescriptions 和 unresolvedMentions 必须全部返回 []；纯图片只由 MomentAgent 做客观整理。
        events 只记录在本次来源时间已经发生或正在发生的具体事情；问候、问答、计划和单纯情绪不生成事件。
        当前内容只回忆本次来源日期之前的经历时 retrospectiveOnly=true，不生成生活记录。
        图片说明只写可直接观察到的内容，不做人脸识别，不猜身份、地点或情绪。
        历史 Tool Result 只能帮助匹配，不能成为当前记录的事实依据。

        只返回一个 JSON 对象，字段必须与下例完全一致；没有内容的数组返回 []，不要改字段名：
        {"retrospectiveOnly":false,"fragment":{"title":"和小明散步","summary":"{{date}}，{{displayName}}和小明在人民公园散步，心情愉快。"},"people":[{"name":"小明","aliases":[],"relationship":"Friend","relationshipKeywords":["最好的朋友"],"summary":"{{date}}，小明和{{displayName}}在人民公园散步。","imageIndexes":[1]}],"places":[{"name":"人民公园","aliases":[],"province":null,"city":null,"summary":"{{date}}，{{displayName}}和小明在人民公园散步。","imageIndexes":[1]}],"events":[{"title":"和小明散步","summary":"{{date}}，{{displayName}}和小明在人民公园散步。","personNames":["小明"],"placeNames":["人民公园"],"imageIndexes":[1]}],"attachmentDescriptions":["户外照片中可见人物和公园环境。"],"unresolvedMentions":[{"kind":"person","mention":"她","reason":"上下文无法唯一确定她是谁。","imageIndexes":[]}]}
        """;
    }

    private static readonly TimeZoneInfo ShanghaiTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
}
