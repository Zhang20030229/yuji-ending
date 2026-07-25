namespace Echora.Api.ModelRuntime;

/// <summary>一次运行使用的不可变模型配置快照。</summary>
/// <param name="WireProtocol">模型线协议。</param>
/// <param name="Endpoint">API 基础地址。</param>
/// <param name="ModelId">供应商模型标识。</param>
/// <param name="ApiKey">运行时使用的 API Key。</param>
/// <param name="Version">模型配置版本。</param>
public sealed record ModelCapabilityConfigurationSnapshot(
    string WireProtocol,
    string Endpoint,
    string ModelId,
    string? ApiKey,
    int Version);
