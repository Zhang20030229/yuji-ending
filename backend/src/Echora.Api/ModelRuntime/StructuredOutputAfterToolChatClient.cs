using Microsoft.Extensions.AI;

namespace Echora.Api.ModelRuntime;

/// <summary>只在当前 MAF Tool Result 已加入历史后启用最终结构化输出。</summary>
internal sealed class StructuredOutputAfterToolChatClient(
    IChatClient innerClient,
    int initialMessageCount) : DelegatingChatClient(innerClient)
{
    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages as IReadOnlyList<ChatMessage> ?? messages.ToArray();
        var hasCurrentToolResult = HasCurrentToolResult(messageList);
        if (options?.ResponseFormat is not null && !hasCurrentToolResult)
        {
            options = options.Clone();
            options.ResponseFormat = null;
        }
        else if (options is not null && hasCurrentToolResult)
        {
            // 每个后台 Agent 只查询一次。Tool Result 已经精确回放后，
            // 最终一轮移除 Tool 定义，只保留 json_object，避免模型重复调用或混入自然语言。
            options = options.Clone();
            options.Tools = null;
            options.ToolMode = ChatToolMode.None;
        }
        return base.GetResponseAsync(messageList, options, cancellationToken);
    }

    /// <summary>忽略完整历史里的旧 Tool Result，只识别本次 FunctionInvoking 循环新追加的结果。</summary>
    private bool HasCurrentToolResult(IReadOnlyList<ChatMessage> messages) =>
        messages.Skip(initialMessageCount)
            .SelectMany(message => message.Contents)
            .Any(content => content is FunctionResultContent);
}
