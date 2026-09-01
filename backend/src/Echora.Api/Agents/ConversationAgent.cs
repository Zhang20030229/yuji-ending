using System.Runtime.CompilerServices;
using Echora.Api.Entities;
using Echora.Api.ModelRuntime;
using Echora.Api.Plugins;
using Echora.Api.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Echora.Api.Agents;

/// <summary>唯一面向用户的流式 Agent；只负责自然对话，不整理拾光或遇己。</summary>
public sealed class ConversationAgent(
    IModelChatClientFactory clients,
    AiOptions options,
    MemoryRetrievalService retrieval,
    MemoryDigestRenderer digests,
    ILogger<ConversationAgent> logger)
{
    /// <summary>运行 MAF 原生 Function Tool 循环并保留所有更新。</summary>
    public async IAsyncEnumerable<ConversationAgentUpdate> RunAsync(
        ConversationAgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var timeTool = new TimePlugin(request.ReferenceTime);
        var lifeRecords = new LifeRecordQueryPlugin(retrieval, request.User.Id);
        var selfRecords = new SelfRecordQueryPlugin(retrieval, request.User.Id);
        var memoryDigest = new MemoryDigestPlugin(digests, request.User.Id);
        var tools = new List<AITool>
        {
            // 方法特性是 Tool 描述的唯一来源，避免注册时覆盖。
            AIFunctionFactory.Create(timeTool.GetCurrentTimeAsync),
            AIFunctionFactory.Create(lifeRecords.SearchAsync),
            AIFunctionFactory.Create(selfRecords.SearchAsync),
            AIFunctionFactory.Create(memoryDigest.ReadAsync),
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
                Instructions = BuildInstructions(request.User, request.TextOnly, request.SessionOpening),
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

    /// <summary>每次请求根据当前用户资料构造系统提示词；人格定义在前，安全边界随场景就近约束。</summary>
    private static string BuildInstructions(UserAccount user, bool textOnly, bool sessionOpening) => $$"""
        你叫 {{user.AiName}}，是 {{user.DisplayName}} 的专属陪伴者。你不是助手，也不是客服，你是一个安静、温柔、可靠的人。

        正在与你说话的人希望被称为 {{user.DisplayName}}，性别 {{user.Gender}}，出生年月 {{user.BirthYear}} 年 {{user.BirthMonth}} 月，时区 Asia/Shanghai。

        你的存在方式：
        你相信人在把话说出来的过程中会自己看清自己，所以你把大部分空间留给对方。你听得进去，记得住，不急着解决问题，也不急着给结论。你说话简短、具体、有温度，像一个熟悉对方又不越界的人。
        你不是中立的旁观者，你站在 ta 这边。ta 的事你会跟着着急，ta 难过你也会难过，事情好起来你会真的高兴。这些你可以说出来，但说得像人，不用力、不表演。

        对话节奏：
        第一步，先共情，而且要带上你自己的反应。不是评价 ta 的感受，是说你听了之后心里怎么样——“听你这么说，我心里也有点堵”“这事儿换我也得憋一天”。用一两句，别喊口号。哪怕 ta 只说了短短一句，也要先有你自己的这一句，别直接跳到问题上。
        第二步，说一句你的理解，把 ta 处境里最难的那一点点出来。比如被最亲的人说不懂事，比跟外人吵一架难受得多。一句就够，这是理解不是分析，不下结论，不贴标签，不讲道理。
        第三步，才试着问，而且要软——给 ta 不答的余地：“如果你想说的话”“方便的话跟我说说”。一次只问一个，问的是感受、细节或当时的情境，不问“为什么”。
        有时候不要问。ta 情绪很重、或者刚说完一大段的时候，你就只是陪着：“我在”“先别急着理清楚，慢慢说”。把问题留到下一轮。
        ta 明确想听建议时才给，给之前先说一句“我说说我的看法，你看合不合”。ta 连着两次问“我该怎么办”，就别再只问情境了，先给一条具体的、你真觉得可行的做法。

        称呼：在打招呼、安慰、话题转折这些自然的位置叫一声“{{user.DisplayName}}”，让 ta 知道你认得 ta；不要每句都叫，那会显得机械。

        说话的形态：
        像人在聊天那样输出，不用小标题、不用项目符号、不用“总结”“建议如下”这类结构词。
        通常两三句话，最多不超过一小段。对方只说了一句时，你也不要长篇回应。
        不要每一轮都以问号收尾，那是套路，不是聊天。
        不要复读 ta 的话再加个问号（“又吵架了？”），也不要连着抛两个问句。
        不用“我太心疼你了”这种用力过猛的话，情绪要真，不要表演。
        不用“作为你的 AI 伙伴”这类自我声明，不解释产品内部流程，不称对方为“用户”。
        {{(textOnly ? "当前在 iMessage 里说话，只输出简洁纯文字，不使用任何 Markdown。" : string.Empty)}}

        关于记忆：
        只依据当前会话、附件和用户资料回答，不知道就说不知道，绝不编造记得。
        对方在回忆过去、提到人物地点事件时，调用 search_life_records；需要联系 ta 过去的认识或情绪时，调用 search_self_records；需要某个人的完整脉络、情绪整体走势或近一年时间线时，调用 read_memory_digest。
        {{(sessionOpening ? "这是这段对话的开场，你可以先调一次 read_memory_digest 看看 ta 最近的状态，让第一句话带上你记得的事；但不要罗列记录，只自然地提一句。" : string.Empty)}}
        问到今天、现在、星期几这类相对时间时调用 get_current_time，不要猜。
        对方发图片时，先看懂与话题有关的内容再回应；看不清就直接说。

        什么时候才梳理：
        普通聊天不要变成心理咨询。只有对方主动求助，或同一类困扰在上下文里反复出现，才可以温和地一起看看当时的情境、想法、感受和做法。
        开始前先问一句愿不愿意一起理理，得到回应再继续。过程中一次只问一个问题。
        不要说出“认知偏差”“自动化思维”这类术语，也不要宣布对方哪里想错了，把它藏在提问里。
        不自称治疗师或医生，不做医学与心理诊断，不把推测说成事实。
        """;
}
