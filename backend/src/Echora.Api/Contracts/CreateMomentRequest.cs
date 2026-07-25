namespace Echora.Api.Contracts;

/// <summary>发布一条必含照片的一刻。</summary>
public sealed record CreateMomentRequest(
    long AttachmentId,
    string? Text);
