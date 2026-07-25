namespace Echora.Api.Contracts;

/// <summary>聊天界面可显示的一段文本、推理或图片。</summary>
public sealed record ConversationPartResponse(
    string Id,
    string Kind,
    string? Text = null,
    long? AssetId = null,
    string? ContentUrl = null,
    string? MediaType = null,
    string? ToolCallId = null,
    string? ToolName = null,
    string? ToolStatus = null);
