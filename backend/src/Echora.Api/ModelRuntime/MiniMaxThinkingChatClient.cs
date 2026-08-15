using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Echora.Api.ModelRuntime;

/// <summary>
/// MiniMax 的 OpenAI 兼容接口把思考内容以 &lt;think&gt;…&lt;/think&gt; 内联在 content 里，
/// 而不是放进独立的 reasoning 字段。本适配器把它还原成 MAF 的 TextReasoningContent，
/// 否则思考过程会作为正文直接流给用户并被持久化。
/// </summary>
internal sealed class MiniMaxThinkingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    private const string OpenTag = "<think>";
    private const string CloseTag = "</think>";

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        foreach (var message in response.Messages.Where(item => item.Role == ChatRole.Assistant))
        {
            var textContents = message.Contents.OfType<TextContent>().ToArray();
            if (textContents.Length == 0) continue;
            var text = string.Concat(textContents.Select(content => content.Text));
            if (!text.Contains(OpenTag, StringComparison.Ordinal)) continue;

            var replacement = SplitThinking(text);
            var firstIndex = message.Contents.IndexOf(textContents[0]);
            foreach (var content in textContents) message.Contents.Remove(content);
            for (var offset = 0; offset < replacement.Count; offset++)
                message.Contents.Insert(firstIndex + offset, replacement[offset]);
            // 原始 SDK 对象仍带内联标签，清除后由后续轮次按 MAF 标准内容重建。
            message.RawRepresentation = null;
        }
        return response;
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        SeparateThinkingAsync(
            base.GetStreamingResponseAsync(messages, options, cancellationToken),
            cancellationToken);

    /// <summary>标签会被拆散在多个增量里，因此必须跨增量维护状态并缓冲可能的半个标签。</summary>
    private async IAsyncEnumerable<ChatResponseUpdate> SeparateThinkingAsync(
        IAsyncEnumerable<ChatResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pending = new StringBuilder();
        var insideThinking = false;
        ChatResponseUpdate? lastUpdate = null;

        await foreach (var update in updates.WithCancellation(cancellationToken))
        {
            lastUpdate = update;
            var textContents = update.Contents.OfType<TextContent>().ToArray();
            var otherContents = update.Contents.Where(content => content is not TextContent).ToArray();

            if (textContents.Length > 0)
            {
                pending.Append(string.Concat(textContents.Select(content => content.Text)));
                foreach (var content in Drain(pending, ref insideThinking))
                {
                    var emitted = update.Clone();
                    emitted.Contents = [content];
                    yield return emitted;
                }
            }

            if (otherContents.Length > 0 || textContents.Length == 0)
            {
                var passthrough = update.Clone();
                passthrough.Contents = otherContents;
                yield return passthrough;
            }
        }

        if (pending.Length == 0) yield break;
        var tail = lastUpdate?.Clone() ?? new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        tail.Contents = [Wrap(pending.ToString(), insideThinking)];
        yield return tail;
    }

    /// <summary>取出所有已经能确定归属的片段，末尾可能的半个标签留在缓冲区。</summary>
    private static List<AIContent> Drain(StringBuilder pending, ref bool insideThinking)
    {
        var contents = new List<AIContent>();
        while (true)
        {
            var text = pending.ToString();
            var tag = insideThinking ? CloseTag : OpenTag;
            var index = text.IndexOf(tag, StringComparison.Ordinal);
            if (index >= 0)
            {
                if (index > 0) contents.Add(Wrap(text[..index], insideThinking));
                pending.Clear();
                pending.Append(text[(index + tag.Length)..]);
                insideThinking = !insideThinking;
                continue;
            }

            // 保留可能是标签开头的后缀，等待下一个增量补全。
            var hold = PartialTagLength(text, tag);
            if (text.Length > hold) contents.Add(Wrap(text[..^hold], insideThinking));
            pending.Clear();
            pending.Append(text[^hold..]);
            return contents;
        }
    }

    /// <summary>把完整文本按标签拆成推理段与正文段，供非流式路径使用。</summary>
    private static List<AIContent> SplitThinking(string text)
    {
        var pending = new StringBuilder(text);
        var insideThinking = false;
        var contents = Drain(pending, ref insideThinking);
        if (pending.Length > 0) contents.Add(Wrap(pending.ToString(), insideThinking));
        return contents;
    }

    /// <summary>空片段不产生内容，避免下游把空字符串当成一次输出。</summary>
    private static AIContent Wrap(string text, bool insideThinking) =>
        insideThinking ? new TextReasoningContent(text) : new TextContent(text);

    /// <summary>返回 text 末尾同时是 tag 真前缀的最长后缀长度。</summary>
    private static int PartialTagLength(string text, string tag)
    {
        var maximum = Math.Min(text.Length, tag.Length - 1);
        for (var length = maximum; length > 0; length--)
        {
            if (string.CompareOrdinal(text, text.Length - length, tag, 0, length) == 0)
                return length;
        }
        return 0;
    }
}
