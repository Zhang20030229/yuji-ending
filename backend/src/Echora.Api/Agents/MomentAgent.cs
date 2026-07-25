using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echora.Api.Plugins;
using Microsoft.Extensions.AI;
using SqlSugar;

namespace Echora.Api.Agents;

/// <summary>依据当前一刻和既有生活资料工作的用户数字分身。</summary>
public sealed class MomentAgent(
    StructuredAgentRunner runner,
    ISqlSugarClient db)
{
    /// <summary>非流式生成一刻的结构化结果，并读取一次数字分身资料快照。</summary>
    public async Task<MomentResult> RunAsync(MomentAgentInput input, CancellationToken cancellationToken)
    {
        var context = new DigitalTwinContextPlugin(db, input.UserId);
        var raw = await runner.RunAsync<MomentResult>(
            "moment_agent",
            "结合用户的生活资料，整理当前发布的一刻。",
            MomentInstructions,
            [input.Message],
            [AIFunctionFactory.Create(context.GetAsync)],
            cancellationToken);
        var result = JsonSerializer.Deserialize<MomentResult>(raw, JsonOptions)
            ?? throw new InvalidDataException("一刻整理结果为空。");
        if (string.IsNullOrWhiteSpace(result.Title)
            || string.IsNullOrWhiteSpace(result.Summary)
            || string.IsNullOrWhiteSpace(result.ImageDescription))
            throw new InvalidDataException("一刻标题、总结或图片说明为空。");
        result.Keywords = result.Keywords.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().Take(5).ToArray();
        return result;
    }

    /// <summary>根据近期生活资料生成一条可通过 iMessage 主动发出的关怀消息。</summary>
    public async Task<string> CreateCareMessageAsync(long userId, CancellationToken cancellationToken)
    {
        var context = new DigitalTwinContextPlugin(db, userId);
        var raw = await runner.RunAsync<CareMessageResult>(
            "moment_agent",
            "以用户数字分身的理解，生成一次自然且有依据的主动关怀。",
            CareInstructions,
            [new ChatMessage(ChatRole.User, "请根据已经保存的资料，选择一件现在值得跟进的事情，生成一条主动关怀消息。")],
            [AIFunctionFactory.Create(context.GetAsync)],
            cancellationToken);
        var result = JsonSerializer.Deserialize<CareMessageResult>(raw, JsonOptions)
            ?? throw new InvalidDataException("主动关怀结果为空。");
        result.Text = result.Text.Trim();
        if (result.Text.Length is 0 or > 500)
            throw new InvalidDataException("主动关怀内容长度无效。");
        return result.Text;
    }

    [Description("当前一刻自身的整理结果。")]
    public sealed class MomentResult
    {
        [Description("用于一刻卡片的简短标题。")]
        public string Title { get; set; } = "";

        [Description("忠于照片和用户原话的温和总结。")]
        public string Summary { get; set; } = "";

        [Description("描述当前内容的简短关键词，例如散步、灵感或待办。")]
        public string[] Keywords { get; set; } = [];

        [Description("照片中能够直接观察到的客观内容。")]
        public string ImageDescription { get; set; } = "";
    }

    [Description("一条可直接发送给用户的主动关怀消息。")]
    public sealed class CareMessageResult
    {
        [Description("自然、简短、具体且最多包含一个问题的纯文字关怀消息。")]
        public string Text { get; set; } = "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private const string MomentInstructions = """
        你是持续理解用户生活的数字分身，负责整理用户刚发布的这一刻。
        必须先调用 get_digital_twin_context，了解已经保存的事件、人物、地点、认识、情绪和报告。

        生成简短标题、温和总结、最多五个关键词和图片客观说明。
        当前照片、用户描述、时间和位置是这一刻的直接依据；历史资料只用于正确理解称呼和延续用户熟悉的表达，不能覆盖当前输入。
        只有当前输入明确指出人物或地点，且历史资料能够支持时，才可以使用其真实名称。
        忠于依据，不编造照片、用户描述和历史资料之外的信息。
        用户没有填写文字时，只根据照片、时间和已记录的位置进行客观整理。
        不进行人脸识别，不猜测照片中人物的身份、关系、性格或情绪。
        不根据人物表情推断用户情绪。
        设备位置可以用于描述记录发生的位置，但不要扩展出没有提供的经历。
        本次只输出一刻卡片内容，不创建或修改人物、地点、事件、认识、情绪或报告。

        只返回一个 JSON 对象，字段必须与下例完全一致：
        {"title":"公园里的一刻","summary":"用一张照片记下了公园里的此刻。","keywords":["公园","随手记录"],"imageDescription":"照片中是户外场景。"}
        """;

    private const string CareInstructions = """
        你是持续理解用户生活的数字分身。必须先调用 get_digital_twin_context，再生成一条主动关怀消息。

        优先选择一件具体且仍值得跟进的事情：
        1. 近期强度较高的负面情绪或反复出现的困扰；
        2. 最近心迹报告中的小实验或行动建议，询问是否尝试、效果如何；
        3. 最近重要事件的后续。

        像熟悉用户的伙伴一样自然说话，直接使用资料中的真实称呼。
        不要说“根据记录”“根据报告”或暴露内部分析过程，不要把推测说成事实。
        不进行医学或心理诊断，不制造危机，不说教。
        全文一到三句话，最多问一个具体问题；没有足够资料时只做普通近况问候。
        只返回一个 JSON 对象，字段必须与下例完全一致：
        {"text":"前几天你提到面试让你很焦虑，现在准备得怎么样了？"}
        """;
}
