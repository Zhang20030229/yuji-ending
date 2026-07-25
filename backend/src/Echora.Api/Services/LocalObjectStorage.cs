namespace Echora.Api.Services;

/// <summary>把附件保存在后端应用目录下，供本地开发和当前自动 E2E 使用。</summary>
public sealed class LocalObjectStorage(IWebHostEnvironment environment, IConfiguration configuration)
    : IObjectStorage
{
    private readonly string _root = ResolveRoot(environment, configuration);

    /// <inheritdoc />
    public async Task UploadAsync(
        string objectKey,
        byte[] bytes,
        string mimeType,
        CancellationToken cancellationToken)
    {
        var path = ResolvePath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
    }

    /// <inheritdoc />
    public Task<byte[]> ReadAsync(string objectKey, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(ResolvePath(objectKey), cancellationToken);

    /// <inheritdoc />
    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(ResolvePath(objectKey));
        return Task.CompletedTask;
    }

    /// <summary>把配置中的相对路径固定到应用根目录，默认使用 Attachments。</summary>
    private static string ResolveRoot(
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        var configured = configuration["Storage:LocalPath"];
        var path = string.IsNullOrWhiteSpace(configured) ? "Attachments" : configured.Trim();
        return Path.GetFullPath(
            Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path));
    }

    /// <summary>限制对象 Key 只能解析到附件根目录内，避免路径穿越。</summary>
    private string ResolvePath(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException("Object key is required.", nameof(objectKey));

        var relativePath = objectKey.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        var rootPrefix = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Object key resolves outside the storage root.");
        return path;
    }
}
