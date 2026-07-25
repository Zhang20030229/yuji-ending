namespace Echora.Api.Contracts;

/// <summary>发送一条文字或图片消息。</summary>
public sealed record CaptureTurnRequest(
    string? Text,
    IReadOnlyList<long>? AssetIds,
    double? Latitude = null,
    double? Longitude = null,
    double? AccuracyMeters = null,
    DateTimeOffset? LocationCapturedAt = null,
    string? Province = null,
    string? City = null,
    string? LocationName = null,
    string? LocationAddress = null);
