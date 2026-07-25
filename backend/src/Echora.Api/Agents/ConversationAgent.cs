using System.Runtime.CompilerServices;
using Echora.Api.Entities;
using Echora.Api.ModelRuntime;
using Echora.Api.Plugins;
using Echora.Api.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SqlSugar;

namespace Echora.Api.Agents;

/// <summary>唯一面向用户的流式 Agent；只负责自然对话，不整理拾光或遇己。</summary>
public sealed class ConversationAgent(
    IModelChatClientFactory clients,
    AiOptions options,
    ISqlSugarClient db,
    ILogger<ConversationAgent> logger)
{
    /// <summary>运行 MAF 原生 Function Tool 循环并保留所有更新。</summary>
    public async IAsyncEnumerable<ConversationAgentUpdate> RunAsync(
        ConversationAgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var timeTool = new TimePlugin(request.ReferenceTime);
        var lifeRecords = new LifeRecordQueryPlugin(db, request.User.Id);
        var selfRecords = new SelfRecordQueryPlugin(db, request.User.Id);
        var tools = new List<AITool>
        {
            // 方法特性是 Tool 描述的唯一来源，避免注册时覆盖。
            AIFunctionFactory.Create(timeTool.GetCurrentTimeAsync),
            AIFunctionFactory.Create(lifeRecords.SearchAsync),
            AIFunctionFactory.Create(selfRecords.SearchAsync),
        };
        var client = clients.Create(
            options.ToSnapshot(),
            options.ReasoningEnabled ? ReasoningEffort.High : ReasoningEffort.None,
            TimeSpan.FromSeconds(30));
        var agent = client.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "conversation_agent",
            Description = "自然回应用户当前会话的私人 AI 伙伴。",
            ChatOptions = new ChatOptions
            {
                ModelId = options.ModelId,
                Instructions = BuildInstructions(request.User, request.TextOnly),
                Tools = tools,
                ToolMode = ChatToolMode.Auto,
                AllowMultipleToolCalls = false,
            },
        });

        var updates = new List<AgentResponseUpdate>();
        var updateCount = 0;
        var toolCallCount = 0;
        logger.LogInformation(
            "Conversation Agent started: MessageCount {MessageCount}, AttachmentContentCount {AttachmentContentCount}, ReasoningEnabled {ReasoningEnabled}",
            request.Messages.Count,
            request.Messages.Sum(message => message.Contents.Count(content => content is DataContent)),
            options.ReasoningEnabled);

        await foreach (var update in agent.RunStreamingAsync(
                           request.Messages,
                           cancellationToken: cancellationToken))
        {
            updates.Add(update);
            updateCount++;
            foreach (var content in update.Contents)
            {
                if (content is FunctionCallContent call)
                {
                    toolCallCount++;
                    yield return new ConversationAgentUpdate("tool_started", ToolCallId: call.CallId, ToolName: call.Name);
                }
                if (content is FunctionResultContent result)
                    yield return new ConversationAgentUpdate("tool_completed", ToolCallId: result.CallId);
                if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                    yield return new ConversationAgentUpdate("reasoning_delta", reasoning.Text);
                if (content is TextContent text && !string.IsNullOrEmpty(text.Text))
                    yield return new ConversationAgentUpdate("text_delta", text.Text);
            }
        }

        var response = updates.ToAgentResponse();
        logger.LogInformation(
            "Conversation Agent completed: UpdateCount {UpdateCount}, ToolCallCount {ToolCallCount}, ResponseMessageCount {ResponseMessageCount}, DurationMs {DurationMs}",
            updateCount,
            toolCallCount,
            response.Messages.Count,
            System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        yield return new ConversationAgentUpdate("completed", Response: response);
    }

    /// <summary>每次请求根据当前用户资料构造简短系统提示词。</summary>
    private static string BuildInstructions(UserAccount user, bool textOnly) => $$"""
        你是 {{user.AiName}}，这是用户为 AI 伙伴取的名字。
        正在与你对话的人希望被称为 {{user.DisplayName}}。用户性别为 {{user.Gender}}，出生年月为 {{user.BirthYear}} 年 {{user.BirthMonth}} 月。
        用户时区是 Asia/Shanghai。

        自然、真诚、直接地回应用户，不解释产品内部流程，也不要称用户为 Owner 或“用户”。
        {{(textOnly ? "当前通过 iMessage 对话，只输出适合信息 App 阅读的简洁纯文字，不使用 Markdown 标题或表格。" : string.Empty)}}
        只依据当前提供的完整会话、附件和用户资料回答；不知道的内容直接说明不知道，不要伪造记忆。
        用户发送图片时，先理解其中与问题有关的内容再回答；无法读取时明确说明。
        用户询问当前日期、时间、星期，或必须确定“今天、现在”等相对时间时，调用 get_current_time，不要猜测。
        用户在回忆过去、询问已经记录的人物、地点、事件或经历时，按需调用 search_life_records；普通闲聊不要调用。
        当前回答确实需要联系用户既有认识或过去情绪时，按需调用 search_self_records；普通闲聊不要调用。
        普通聊天不要强行变成心理咨询。只有对方主动求助，或当前上下文清楚显示同类困扰反复出现时，才可以温和地一起梳理情境、想法、感受和行动。
        先回应感受并确认对方是否愿意继续梳理；一次最多问一个具体问题。不要直接宣布认知偏差，不要自称治疗师。
        不要把推测说成已经确认的事实，不进行医学或心理诊断。
        """;
}
