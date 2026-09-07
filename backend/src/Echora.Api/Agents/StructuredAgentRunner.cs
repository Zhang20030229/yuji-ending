using Echora.Api.ModelRuntime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Echora.Api.Agents;

/// <summary>后台结构化智能体共用的非流式 JSON Mode 调用入口。</summary>
public sealed class StructuredAgentRunner(
    IModelChatClientFactory clients,
    AiOptions options,
    IConfiguration configuration,
    ILogger<StructuredAgentRunner> logger)
{
    // Workflow 负责固定分支编排；同一作用域内的 MiMo 请求串行执行，沿用当前已验证的稳定调用方式。
    private readonly SemaphoreSlim _modelGate = new(1, 1);

    /// <summary>运行一次完整 Function Tool 循环，并返回最后一条 Assistant JSON。</summary>
    public async Task<string> RunAsync<TResult>(
        string name,
        string description,
        string instructions,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<AITool> tools,
        CancellationToken cancellationToken,
        float? temperature = null)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var requestMessages = messages.ToArray();
        IChatClient client = clients.Create(
            options.ToSnapshot(),
            ReasoningEffort.None,
            TimeSpan.FromSeconds(30));

        // 首轮先完成查询 Tool；模型取得真实 Function Result 后再要求 JSON Object。
        if (tools.Count > 0)
            client = new StructuredOutputAfterToolChatClient(client, requestMessages.Length);
        var agent = client.AsAIAgent(new ChatClientAgentOptions
        {
            Name = name,
            Description = description,
            ChatOptions = new ChatOptions
            {
                ModelId = options.ModelId,
                Instructions = instructions,
                Tools = tools.ToList(),
                ToolMode = tools.Count == 0 ? ChatToolMode.None : ChatToolMode.Auto,
                AllowMultipleToolCalls = false,
                ResponseFormat = ChatResponseFormat.Json,
                Temperature = temperature,
            },
        });

        logger.LogInformation(
            "Subagent started: Agent {Agent}, MessageCount {MessageCount}, ToolCount {ToolCount}",
            name,
            requestMessages.Length,
            tools.Count);
        await _modelGate.WaitAsync(cancellationToken);
        AgentResponse response;
        try
        {
            response = await agent.RunAsync(requestMessages, cancellationToken: cancellationToken);
        }
        finally
        {
            _modelGate.Release();
        }

        var toolCallCount = response.Messages.Sum(message =>
            message.Contents.Count(content => content is FunctionCallContent));
        var toolResultCount = response.Messages.Sum(message =>
            message.Contents.Count(content => content is FunctionResultContent));
        if (tools.Count > 0 && (toolCallCount == 0 || toolResultCount == 0))
            throw new InvalidDataException("模型没有完成必需的查询 Tool 调用链。");

        var finalMessage = response.Messages.LastOrDefault(message =>
            message.Role == ChatRole.Assistant
            && !message.Contents.Any(content => content is FunctionCallContent));
        var raw = string.Concat(
            finalMessage?.Contents.OfType<TextContent>().Select(content => content.Text) ?? []);
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidDataException("模型没有返回最终 JSON 内容。");

        // 模型有时用 markdown 代码围栏（```json ... ```）包裹 JSON，剥离后再返回，避免反序列化失败。
        raw = StripMarkdownFence(raw);

        // 仅允许在隔离诊断环境查看原始输出，普通日志不记录用户正文。
        if (bool.TryParse(configuration["AI:DiagnosticRawOutput"], out var enabled) && enabled)
            logger.LogWarning("Subagent raw diagnostic: Agent {Agent}, Raw {Raw}", name, raw);
        logger.LogInformation(
            "Subagent completed: Agent {Agent}, ToolCallCount {ToolCallCount}, ToolResultCount {ToolResultCount}, ResponseCharacterCount {ResponseCharacterCount}, DurationMs {DurationMs}",
            name,
            toolCallCount,
            toolResultCount,
            raw.Length,
            System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        return raw;
    }

    /// <summary>剥离模型输出可能包裹的 markdown 代码围栏（```json ... ``` 或 ``` ... ```）。</summary>
    private static string StripMarkdownFence(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("```")) return text;
        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0) return text;
        text = text[(firstNewline + 1)..].Trim();
        if (text.EndsWith("```")) text = text[..^3].Trim();
        return text;
    }
}
