namespace Echora.Api.Contracts;

/// <summary>用户驳回一条认识；原聊天或一刻不会被修改。</summary>
public sealed class RejectRecognitionRequest
{
    /// <summary>可选的驳回说明，会作为负面清单的一部分提供给归纳模型。</summary>
    public string? Note { get; set; }
}
