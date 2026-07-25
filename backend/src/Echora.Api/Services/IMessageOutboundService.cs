using System.Net.Http.Headers;
using System.Net.Http.Json;
using Echora.Api.Contracts;

namespace Echora.Api.Services;

/// <summary>供后续业务功能调用的主动 iMessage 发送基础服务。</summary>
public sealed class IMessageOutboundService(
    HttpClient httpClient,
    IMessageBindingService bindings,
    ConversationService conversations,
    IMessageOptions options)
{
    /// <summary>向已绑定用户发送纯文字，并把成功发送的消息写入 iMessage 历史。</summary>
    public async Task<IMessageOutboundReceipt> SendAsync(
        long userId,
        string text,
        CancellationToken cancellationToken = default)
    {
        text = text.Trim();
        if (text.Length is 0 or > 20000)
            throw new ArgumentException("iMessage 文字长度必须为 1 到 20000 个字符。", nameof(text));
        if (!Uri.TryCreate(options.GatewayBaseUrl, UriKind.Absolute, out var gateway))
            throw new InvalidOperationException("iMessage 网关地址无效。");

        var senderId = await bindings.GetBoundSenderIdAsync(userId, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(gateway, "/internal/messages/send"))
        {
            Content = JsonContent.Create(new GatewaySendRequest(senderId, text)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.InternalSecret);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"iMessage 网关发送失败（{(int)response.StatusCode}）。");
        var receipt = await response.Content.ReadFromJsonAsync<IMessageOutboundReceipt>(
                          cancellationToken: cancellationToken)
                      ?? throw new HttpRequestException("iMessage 网关没有返回发送回执。");

        await conversations.CaptureIMessageOutboundAsync(
            userId,
            receipt.SpaceId,
            text,
            receipt.SentAt,
            cancellationToken);
        return receipt;
    }

    /// <summary>网关内部主动发送请求。</summary>
    private sealed record GatewaySendRequest(string SenderId, string Text);
}
