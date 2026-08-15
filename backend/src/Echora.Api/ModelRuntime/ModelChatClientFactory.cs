using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Echora.Api.ModelRuntime;

/// <summary>使用 OpenAI Chat Completions 协议创建 MAF 所需的聊天客户端。</summary>
public sealed class ModelChatClientFactory(ILoggerFactory? loggerFactory = null) : IModelChatClientFactory
{
    /// <inheritdoc />
    [Experimental("OPENAI001")]
    public IChatClient Create(
        ModelCapabilityConfigurationSnapshot configuration,
        ReasoningEffort reasoningEffort = ReasoningEffort.High,
        TimeSpan? networkTimeout = null)
    {
        if (configuration.WireProtocol != "openai_chat_completions")
            throw new ConversationRuntimeException("model.protocol_not_supported", "该聊天模型协议尚未实现。");
        if (!Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
            throw new ConversationRuntimeException("model.invalid_configuration", "聊天模型地址无效。");
        if (string.IsNullOrWhiteSpace(configuration.ModelId)
            || string.IsNullOrWhiteSpace(configuration.ApiKey))
            throw new ConversationRuntimeException("model.invalid_configuration", "聊天模型缺少 API Key。");

        var options = new OpenAIClientOptions
        {
            Endpoint = endpoint,
            // Provider SDK 默认会在一次产品操作内再试 3 次，网络失败会被包成 AggregateException。
            // ECHORA 禁用这层不可见重试：对话立即如实报错，后台只由有状态的 Hangfire 有限重试。
            RetryPolicy = new ClientRetryPolicy(0),
        };
        var vendor = ModelVendorResolver.Resolve(endpoint);
        if (vendor == ModelVendor.Mimo)
            options.AddPolicy(new MimoSseNullFixPolicy(), PipelinePosition.PerCall);
        if (networkTimeout is not null)
            options.NetworkTimeout = networkTimeout.Value;
        IChatClient client = new OpenAIClient(new ApiKeyCredential(configuration.ApiKey), options)
            .GetChatClient(configuration.ModelId)
            .AsIChatClient();
        if (vendor == ModelVendor.Mimo)
            client = new MimoChatClientAdapter(
                client,
                loggerFactory?.CreateLogger<MimoChatClientAdapter>());
        if (vendor == ModelVendor.MiniMax)
            client = new MiniMaxThinkingChatClient(client);
        return client
            .AsBuilder()
            .ConfigureOptions(chatOptions =>
            {
                switch (vendor)
                {
                    case ModelVendor.Mimo:
                        // MiMo 官方 Chat Completions 使用 thinking.type，不使用 OpenAI reasoning_effort。
                        chatOptions.Reasoning = null;
                        chatOptions.RawRepresentationFactory = _ => CreateMimoCompletionOptions(reasoningEffort);
                        break;
                    case ModelVendor.MiniMax:
                        // MiniMax-M3 同样使用私有 thinking.type，不接受 reasoning_effort。
                        chatOptions.Reasoning = null;
                        chatOptions.RawRepresentationFactory = _ => CreateMiniMaxCompletionOptions(reasoningEffort);
                        break;
                    default:
                        chatOptions.Reasoning = new() { Effort = reasoningEffort };
                        break;
                }
            })
            .Build();
    }

    /// <summary>创建携带 MiniMax 私有 thinking 字段的选项；其余标准参数仍由 MEAI 补齐。</summary>
#pragma warning disable OPENAI001
    internal static ChatCompletionOptions CreateMiniMaxCompletionOptions(ReasoningEffort reasoningEffort)
    {
        // 实测 MiniMax-M3 接受 adaptive 与 disabled 两种取值。
        var type = reasoningEffort == ReasoningEffort.None ? "disabled" : "adaptive";
        return ModelReaderWriter.Read<ChatCompletionOptions>(
                   BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(new
                   {
                       thinking = new { type },
                   })),
                   ModelReaderWriterOptions.Json)
               ?? throw new InvalidOperationException("无法构造 MiniMax Chat Completions 参数。");
    }
#pragma warning restore OPENAI001

    /// <summary>创建保留 MiMo 私有 thinking 字段、同时允许 MEAI 补齐其他标准参数的选项。</summary>
#pragma warning disable OPENAI001
    internal static ChatCompletionOptions CreateMimoCompletionOptions(ReasoningEffort reasoningEffort)
    {
        var type = reasoningEffort == ReasoningEffort.None ? "disabled" : "enabled";
        var options = ModelReaderWriter.Read<ChatCompletionOptions>(
                          BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(new
                          {
                              thinking = new { type },
                          })),
                          ModelReaderWriterOptions.Json)
                      ?? throw new InvalidOperationException("无法构造 MiMo Chat Completions 参数。");
        // MiMo 实测在思考模式下并行调用时可能把 <tool_call> 写进 content、却不给 tool_calls；
        // ECHORA 的理解工具本就有顺序依赖，显式禁用并行后恢复标准 OpenAI FunctionCall 字段。
        options.AllowParallelToolCalls = false;
        return options;
    }
#pragma warning restore OPENAI001
}
