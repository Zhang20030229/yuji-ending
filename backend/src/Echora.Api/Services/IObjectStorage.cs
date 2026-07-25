namespace Echora.Api.Services;

/// <summary>附件原始字节的最小存储能力，本地目录和腾讯云 COS 共用同一业务调用链。</summary>
public interface IObjectStorage
{
    /// <summary>保存一个对象。</summary>
    Task UploadAsync(
        string objectKey,
        byte[] bytes,
        string mimeType,
        CancellationToken cancellationToken);

    /// <summary>读取一个对象的完整字节。</summary>
    Task<byte[]> ReadAsync(string objectKey, CancellationToken cancellationToken);

    /// <summary>删除一个对象；对象不存在时也视为成功。</summary>
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}
