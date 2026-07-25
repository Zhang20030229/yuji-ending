using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Echora.Api.ModelRuntime;

/// <summary>修正 MiMo 与 OpenAI Chat Completions 工具协议的已知差异。</summary>
internal sealed partial class MimoChatClientAdapter(
    IChatClient innerClient,
    ILogger? logger = null) : DelegatingChatClient(innerClient)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var prepared = PrepareMessages(messages);
        logger?.LogInformation(
            "MiMo non-streaming round started: MessageCount {MessageCount}, ToolCount {ToolCount}",
            prepared.Count,
            options?.Tools?.Count ?? 0);
        try
        {
            var response = await base.GetResponseAsync(prepared, options, cancellationToken);
            NormalizeTextToolCalls(response, options);
            logger?.LogInformation(
                "MiMo non-streaming round completed: MessageCount {MessageCount}, FunctionCallCount {FunctionCallCount}, DurationMs {DurationMs}",
                prepared.Count,
                response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Count(),
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return response;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(
                "MiMo non-streaming round failed: MessageCount {MessageCount}, ExceptionType {ExceptionType}, DurationMs {DurationMs}",
                prepared.Count,
                exception.GetType().FullName,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw;
        }
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        NormalizeStreamingTextToolCallsAsync(
            base.GetStreamingResponseAsync(PrepareMessages(messages), options, cancellationToken),
            options,
            cancellationToken);

    /// <summary>把 MiMo 在流开头写入正文的 Tool 协议恢复为 MAF FunctionCall。</summary>
    private async IAsyncEnumerable<ChatResponseUpdate> NormalizeStreamingTextToolCallsAsync(
        IAsyncEnumerable<ChatResponseUpdate> updates,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var allowedTools = options?.Tools?
            .OfType<AIFunctionDeclaration>()
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (allowedTools is not { Count: > 0 })
        {
            await foreach (var update in updates.WithCancellation(cancellationToken))
                yield return update;
            yield break;
        }

        var pendingText = new System.Text.StringBuilder();
        ChatResponseUpdate? lastTextUpdate = null;
        var protocolCandidate = true;
        await foreach (var update in updates.WithCancellation(cancellationToken))
        {
            var textContents = update.Contents.OfType<TextContent>().ToArray();
            var otherContents = update.Contents.Where(content => content is not TextContent).ToArray();
            if (otherContents.Length > 0)
            {
                var passthrough = update.Clone();
                passthrough.Contents = otherContents;
                yield return passthrough;
            }

            if (textContents.Length == 0)
            {
                if (otherContents.Length == 0 && !protocolCandidate)
                    yield return update;
                continue;
            }

            var text = string.Concat(textContents.Select(content => content.Text));
            if (!protocolCandidate)
            {
                var passthrough = update.Clone();
                passthrough.Contents = textContents;
                yield return passthrough;
                continue;
            }

            pendingText.Append(text);
            lastTextUpdate = update;
            var candidate = pendingText.ToString().TrimStart();
            if ("<tool_call>".StartsWith(candidate, StringComparison.Ordinal))
                continue;
            if (!candidate.StartsWith("<tool_call>", StringComparison.Ordinal))
            {
                protocolCandidate = false;
                var passthrough = update.Clone();
                passthrough.Contents = [new TextContent(pendingText.ToString())];
                pendingText.Clear();
                yield return passthrough;
            }
        }

        if (!protocolCandidate || pendingText.Length == 0)
            yield break;

        var calls = ParseLeadingTextToolCalls(pendingText.ToString(), allowedTools);
        if (calls is null)
        {
            logger?.LogWarning(
                "MiMo streaming text tool protocol was rejected: TextCharacterCount {TextCharacterCount}",
                pendingText.Length);
            var rejected = lastTextUpdate?.Clone() ?? new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
            rejected.Contents = [new TextContent(pendingText.ToString())];
            yield return rejected;
            yield break;
        }

        var normalized = lastTextUpdate?.Clone() ?? new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        normalized.Contents = calls.Cast<AIContent>().ToArray();
        normalized.RawRepresentation = null;
        normalized.FinishReason = Microsoft.Extensions.AI.ChatFinishReason.ToolCalls;
        logger?.LogInformation(
            "MiMo streaming text tool protocol normalized: ToolCallCount {ToolCallCount}, ToolNames {ToolNames}, ProviderTextCharacterCount {ProviderTextCharacterCount}",
            calls.Count,
            calls.Select(call => call.Name).ToArray(),
            pendingText.Length);
        yield return normalized;
    }

    /// <summary>只改写同时包含工具调用和可见推理的 Assistant 消息，其余消息保持原对象。</summary>
    internal static IReadOnlyList<AiChatMessage> PrepareMessages(IEnumerable<AiChatMessage> messages)
    {
        var prepared = messages.ToArray();
        for (var index = 0; index < prepared.Length; index++)
        {
            var message = prepared[index];
            if (message.Role != ChatRole.Assistant || message.RawRepresentation is AssistantChatMessage)
                continue;

            var calls = message.Contents.OfType<FunctionCallContent>().ToArray();
            var reasoning = string.Concat(message.Contents
                .OfType<TextReasoningContent>()
                .Select(content => content.Text));
            if (calls.Length == 0 || string.IsNullOrEmpty(reasoning))
                continue;

            var json = JsonSerializer.SerializeToUtf8Bytes(new
            {
                role = "assistant",
                content = string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text)),
                reasoning_content = reasoning,
                tool_calls = calls.Select(call => new
                {
                    id = call.CallId,
                    type = "function",
                    function = new
                    {
                        name = call.Name,
                        // MiMo 会把 arguments 当成映射读取；缺失参数必须回放为空对象而不是 null。
                        arguments = call.Arguments is null ? "{}" : JsonSerializer.Serialize(call.Arguments),
                    },
                }),
            });
            var raw = ModelReaderWriter.Read<AssistantChatMessage>(
                BinaryData.FromBytes(json),
                ModelReaderWriterOptions.Json)
                ?? throw new InvalidOperationException("无法构造 MiMo 工具调用历史。");
            prepared[index] = message.Clone();
            prepared[index].RawRepresentation = raw;
        }

        // ponytail: 仅兼容 MiMo 当前协议差异；供应商稳定返回标准 tool_calls 后删除本适配器。
        return prepared;
    }

    /// <summary>将 MiMo 偶发写入正文的完整工具协议转换为 MAF FunctionCall。</summary>
    private void NormalizeTextToolCalls(ChatResponse response, ChatOptions? options)
    {
        var allowedTools = options?.Tools?
            .OfType<AIFunctionDeclaration>()
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (allowedTools is not { Count: > 0 })
            return;

        foreach (var message in response.Messages.Where(item => item.Role == ChatRole.Assistant))
        {
            var textContents = message.Contents.OfType<TextContent>().ToArray();
            if (textContents.Length == 0)
                continue;

            var text = string.Concat(textContents.Select(content => content.Text));
            var calls = ParseTextToolCalls(text, allowedTools);
            if (calls is null)
            {
                if (text.TrimStart().StartsWith("<tool_call>", StringComparison.Ordinal))
                    logger?.LogWarning(
                        "MiMo text tool protocol was rejected because it was malformed or referenced an unknown tool: TextCharacterCount {TextCharacterCount}",
                        text.Length);
                continue;
            }

            foreach (var content in textContents)
                message.Contents.Remove(content);
            foreach (var call in calls)
                message.Contents.Add(call);

            // 原始 SDK 对象仍是文本协议，必须清除，下一轮由 PrepareMessages 重建标准工具历史。
            message.RawRepresentation = null;
            logger?.LogInformation(
                "MiMo text tool protocol normalized: ToolCallCount {ToolCallCount}, ToolNames {ToolNames}, ArgumentShapes {ArgumentShapes}",
                calls.Count,
                calls.Select(call => call.Name).ToArray(),
                calls.Select(DescribeCallShape).ToArray());
        }
    }

    /// <summary>只记录 Function 参数名与 JSON 类型，避免诊断日志泄露参数值。</summary>
    private static string DescribeCallShape(FunctionCallContent call) =>
        $"{call.Name}({string.Join(',', call.Arguments?.Select(argument =>
            $"{argument.Key}:{DescribeArgumentType(argument.Value)}") ?? [])})";

    /// <summary>把参数值收敛为安全类型名称，不调用可能包含正文的 ToString。</summary>
    private static string DescribeArgumentType(object? value) => value switch
    {
        null => "Null",
        JsonElement element => element.ValueKind.ToString(),
        string => "String",
        System.Collections.IEnumerable => "Array",
        _ => value.GetType().Name,
    };

    /// <summary>严格解析完全由已注册工具调用组成的 MiMo 文本协议。</summary>
    internal static IReadOnlyList<FunctionCallContent>? ParseTextToolCalls(
        string text,
        IReadOnlySet<string> allowedTools)
    {
        var calls = new List<FunctionCallContent>();
        var offset = 0;
        while (offset < text.Length)
        {
            var match = ToolCallPattern().Match(text, offset);
            if (!match.Success || match.Index != offset)
                return null;

            var name = match.Groups["name"].Value;
            if (!allowedTools.Contains(name))
                return null;

            var arguments = ParseArguments(match.Groups["body"].Value);
            if (arguments is null)
                return null;

            calls.Add(new FunctionCallContent($"call_mimo_{Guid.NewGuid():N}", name, arguments));
            offset += match.Length;
        }

        return calls.Count == 0 ? null : calls;
    }

    /// <summary>解析流开头的一个或多个完整 Tool 块；Tool 后的抢答正文不参与本轮。</summary>
    internal static IReadOnlyList<FunctionCallContent>? ParseLeadingTextToolCalls(
        string text,
        IReadOnlySet<string> allowedTools)
    {
        var calls = new List<FunctionCallContent>();
        var offset = 0;
        while (offset < text.Length)
        {
            var match = ToolCallPattern().Match(text, offset);
            if (!match.Success || match.Index != offset)
                break;
            var name = match.Groups["name"].Value;
            if (!allowedTools.Contains(name)) return null;
            var arguments = ParseArguments(match.Groups["body"].Value);
            if (arguments is null) return null;
            calls.Add(new FunctionCallContent($"call_mimo_{Guid.NewGuid():N}", name, arguments));
            offset += match.Length;
        }

        return calls.Count == 0 ? null : calls;
    }

    /// <summary>解析一个工具调用中的参数；JSON 值保留类型，普通文本按字符串处理。</summary>
    private static Dictionary<string, object?>? ParseArguments(string body)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        var offset = 0;
        while (offset < body.Length)
        {
            var match = ParameterPattern().Match(body, offset);
            if (!match.Success || match.Index != offset)
                return string.IsNullOrWhiteSpace(body[offset..]) ? arguments : null;

            var name = match.Groups["name"].Value;
            if (!arguments.TryAdd(name, ParseValue(match.Groups["value"].Value.Trim())))
                return null;
            offset += match.Length;
        }

        return arguments;
    }

    /// <summary>优先按 JSON 读取参数类型，非 JSON 文本保持字符串。</summary>
    private static object? ParseValue(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return WebUtility.HtmlDecode(value);
        }
    }

    [GeneratedRegex(@"\G\s*<tool_call>\s*<function=(?<name>[A-Za-z0-9_-]{1,64})>\s*(?<body>.*?)\s*</function>\s*</tool_call>\s*", RegexOptions.Singleline, 1000)]
    private static partial Regex ToolCallPattern();

    [GeneratedRegex(@"\G\s*<parameter=(?<name>[A-Za-z_][A-Za-z0-9_]*)>(?<value>.*?)</parameter>\s*", RegexOptions.Singleline, 1000)]
    private static partial Regex ParameterPattern();
}
