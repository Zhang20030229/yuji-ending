using COSXML;
using COSXML.Auth;
using COSXML.Model.Object;

namespace Echora.Api.Services;

/// <summary>封装腾讯云官方 .NET SDK 的上传、读取和删除操作，供后续手动验证并启用。</summary>
public sealed class CosObjectStorage(CosOptions options) : IObjectStorage
{
    /// <summary>官方建议复用服务实例；延迟创建避免未使用附件时阻断其他功能。</summary>
    private readonly Lazy<CosXml> _client = new(() => CreateClient(options));

    /// <summary>把已验证的原始图片上传到私有 COS。</summary>
    public Task UploadAsync(
        string objectKey,
        byte[] bytes,
        string mimeType,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var request = new PutObjectRequest(options.Bucket, objectKey, bytes);
            request.SetRequestHeader("Content-Type", mimeType);
            _client.Value.PutObject(request);
        }, cancellationToken);
    }

    /// <summary>把私有 COS 对象读取到内存，供授权下载和模型输入使用。</summary>
    public Task<byte[]> ReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var result = _client.Value.GetObject(new GetObjectBytesRequest(options.Bucket, objectKey));
            return result.content;
        }, cancellationToken);
    }

    /// <summary>删除一个 COS 对象；失败时由调用方决定是否进入 Hangfire 补偿。</summary>
    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            _client.Value.DeleteObject(new DeleteObjectRequest(options.Bucket, objectKey));
        }, cancellationToken);
    }

    /// <summary>按腾讯云官方推荐方式创建可复用的 COS 服务实例。</summary>
    public static CosXml CreateClient(CosOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Region)
            || string.IsNullOrWhiteSpace(options.Bucket)
            || string.IsNullOrWhiteSpace(options.SecretId)
            || string.IsNullOrWhiteSpace(options.SecretKey))
            throw new InvalidOperationException("COS configuration is incomplete.");

        var config = new CosXmlConfig.Builder()
            .SetRegion(options.Region)
            .Build();
        var credentials = new DefaultQCloudCredentialProvider(
            options.SecretId,
            options.SecretKey,
            600);
        return new CosXmlServer(config, credentials);
    }
}
