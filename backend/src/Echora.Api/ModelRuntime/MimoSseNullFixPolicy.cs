using System.ClientModel.Primitives;

namespace Echora.Api.ModelRuntime;

/// <summary>在 OpenAI SDK 解析 MiMo SSE 前移除后续 Tool 增量中的空身份字段。</summary>
internal sealed class MimoSseNullFixPolicy : PipelinePolicy
{
    /// <inheritdoc />
    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        ProcessNext(message, pipeline, currentIndex);
        WrapStreamingResponse(message);
    }

    /// <inheritdoc />
    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        await ProcessNextAsync(message, pipeline, currentIndex);
        WrapStreamingResponse(message);
    }

    /// <summary>只包装尚未缓冲的 SSE 响应，避免改变普通 JSON 响应的读取方式。</summary>
    private static void WrapStreamingResponse(PipelineMessage message)
    {
        if (message.BufferResponse) return;
        if (message.Response?.ContentStream is { } stream)
            message.Response.ContentStream = new MimoSseNullFixStream(stream);
    }
}
