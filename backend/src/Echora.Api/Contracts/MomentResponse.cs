namespace Echora.Api.Contracts;

/// <summary>一刻列表和详情共用的公开数据。</summary>
public sealed record MomentResponse(
    long Id,
    long AttachmentId,
    string? Text,
    string? Title,
    string? Summary,
    IReadOnlyList<string> Keywords,
    DateTimeOffset CapturedAt,
    DateTimeOffset PublishedAt,
    string? LocationName,
    string? LocationAddress,
    string? Province,
    string? City,
    string Status,
    string? ErrorMessage,
    string ImageUrl,
    string? ImageDescription);
