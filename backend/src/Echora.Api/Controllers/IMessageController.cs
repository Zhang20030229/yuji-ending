using System.Text;
using Echora.Api.Agents;
using Echora.Api.Authentication;
using Echora.Api.Contracts;
using Echora.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Echora.Api.Controllers;

/// <summary>管理用户 iMessage 绑定，并接收受信 Spectrum 网关的文字消息。</summary>
[ApiController]
public sealed class IMessageController(
    IMessageBindingService bindings,
    AttachmentService attachments,
    ConversationService conversations,
    MomentAgent momentAgent,
    IMessageOutboundService outbound) : ControllerBase
{
    /// <summary>读取当前账号的 iMessage 绑定状态。</summary>
    [Authorize]
    [HttpGet("api/imessage/binding")]
    public async Task<ActionResult<IMessageBindingResponse>> GetBinding(
        CancellationToken cancellationToken) =>
        Ok(await bindings.GetAsync(User.GetRequiredId(), cancellationToken));

    /// <summary>生成当前账号专用的一次性绑定码。</summary>
    [Authorize]
    [HttpPost("api/imessage/binding/code")]
    public async Task<ActionResult<IMessageBindingCodeResponse>> CreateBindingCode(
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await bindings.CreateCodeAsync(User.GetRequiredId(), cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(CreateProblem(exception.Message, StatusCodes.Status409Conflict));
        }
    }

    /// <summary>解除当前账号与 iMessage 发件身份的绑定。</summary>
    [Authorize]
    [HttpDelete("api/imessage/binding")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        await bindings.DisconnectAsync(User.GetRequiredId(), cancellationToken);
        return NoContent();
    }

    /// <summary>根据近期生活资料生成一条关怀，并通过已绑定的 iMessage 主动发送。</summary>
    [Authorize]
    [HttpPost("api/imessage/care")]
    public async Task<ActionResult<IMessageOutboundReceipt>> SendCare(
        CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredId();
        var binding = await bindings.GetAsync(userId, cancellationToken);
        if (!binding.Enabled || !binding.IsBound)
            return Conflict(CreateProblem("请先在设置中连接 iMessage。", StatusCodes.Status409Conflict));

        try
        {
            var text = await momentAgent.CreateCareMessageAsync(userId, cancellationToken);
            return Ok(await outbound.SendAsync(userId, text, cancellationToken));
        }
        catch (InvalidDataException exception)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                CreateProblem(exception.Message, StatusCodes.Status502BadGateway));
        }
        catch (HttpRequestException exception)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                CreateProblem(exception.Message, StatusCodes.Status502BadGateway));
        }
    }

    /// <summary>接收 Spectrum 网关消息，解析绑定并调用统一的 ConversationAgent。</summary>
    [AllowAnonymous]
    [HttpPost("internal/imessage/messages")]
    public async Task<ActionResult<IMessageInboundResponse>> Receive(
        IMessageInboundRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsGatewayAuthorized()) return Unauthorized();

        var validationError = Validate(request);
        if (validationError is not null)
            return BadRequest(CreateProblem(validationError, StatusCodes.Status400BadRequest));

        var resolution = await bindings.ResolveAsync(
            request.SenderId.Trim(),
            request.Text,
            cancellationToken);
        if (resolution.Reply is not null)
            return Ok(new IMessageInboundResponse("reply", resolution.Reply));
        if (resolution.UserId is not long userId)
            return Ok(new IMessageInboundResponse(
                "reply",
                "请先在遇己的设置中生成绑定码，再发送“绑定 空格 绑定码”。"));

        try
        {
            var receipt = await conversations.CaptureIMessageAsync(
                userId,
                request.SpaceId,
                request.MessageId,
                request.Text,
                request.SentAt,
                cancellationToken);
            if (receipt.IsDuplicate)
                return Ok(new IMessageInboundResponse("ignore"));

            return Ok(await BuildReplyAsync(userId, receipt.TurnId, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(CreateProblem(exception.Message, StatusCodes.Status400BadRequest));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(CreateProblem(exception.Message, StatusCodes.Status409Conflict));
        }
    }

    /// <summary>接收 Spectrum 图片原始字节，并复用聊天图片、Agent 和后台分析链路。</summary>
    [AllowAnonymous]
    [HttpPost("internal/imessage/attachments")]
    [RequestSizeLimit(AttachmentService.MaxBytes * 10)]
    public async Task<ActionResult<IMessageInboundResponse>> ReceiveAttachments(
        [FromForm] IMessageAttachmentInboundRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsGatewayAuthorized()) return Unauthorized();
        var validationError = Validate(request);
        if (validationError is not null)
            return BadRequest(CreateProblem(validationError, StatusCodes.Status400BadRequest));

        var userId = await bindings.ResolveBoundUserAsync(request.SenderId.Trim(), cancellationToken);
        if (userId is null)
            return Ok(new IMessageInboundResponse(
                "reply",
                "请先在遇己的设置中生成绑定码，再发送“绑定 空格 绑定码”。"));

        var uploadedIds = new List<long>(request.Files.Count);
        try
        {
            foreach (var file in request.Files)
            {
                var uploaded = await attachments.UploadAsync(userId.Value, file, cancellationToken);
                uploadedIds.Add(uploaded.Id);
            }

            var receipt = await conversations.CaptureIMessageAsync(
                userId.Value,
                request.SpaceId,
                request.MessageId,
                request.Text,
                request.SentAt,
                cancellationToken,
                uploadedIds);
            if (receipt.IsDuplicate)
            {
                await DeleteUploadedAsync(userId.Value, uploadedIds, cancellationToken);
                return Ok(new IMessageInboundResponse("ignore"));
            }
            return Ok(await BuildReplyAsync(userId.Value, receipt.TurnId, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            await DeleteUploadedAsync(userId.Value, uploadedIds, cancellationToken);
            return BadRequest(CreateProblem(exception.Message, StatusCodes.Status400BadRequest));
        }
        catch (InvalidOperationException exception)
        {
            await DeleteUploadedAsync(userId.Value, uploadedIds, cancellationToken);
            return Conflict(CreateProblem(exception.Message, StatusCodes.Status409Conflict));
        }
        catch
        {
            await DeleteUploadedAsync(userId.Value, uploadedIds, cancellationToken);
            throw;
        }
    }

    /// <summary>限制网关输入为当前 MVP 支持的短文本和不透明标识。</summary>
    private static string? Validate(IMessageInboundRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId) || request.MessageId.Length > 80)
            return "消息标识无效。";
        if (string.IsNullOrWhiteSpace(request.SpaceId) || request.SpaceId.Length > 500)
            return "会话标识无效。";
        if (string.IsNullOrWhiteSpace(request.SenderId) || request.SenderId.Length > 500)
            return "发件人标识无效。";
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 20000)
            return "当前只支持 1 到 20000 个字符的文字消息。";
        return null;
    }

    /// <summary>验证图片消息的外部标识、数量和可选文字。</summary>
    private static string? Validate(IMessageAttachmentInboundRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId) || request.MessageId.Length > 80)
            return "消息标识无效。";
        if (string.IsNullOrWhiteSpace(request.SpaceId) || request.SpaceId.Length > 500)
            return "会话标识无效。";
        if (string.IsNullOrWhiteSpace(request.SenderId) || request.SenderId.Length > 500)
            return "发件人标识无效。";
        if (request.Text?.Length > 20000)
            return "文字消息不能超过 20000 个字符。";
        if (request.Files.Count is < 1 or > 10)
            return "一条 iMessage 必须包含 1 到 10 张图片。";
        if (request.Files.Any(file => file.Length is <= 0 or > AttachmentService.MaxBytes))
            return "单张图片必须小于 25 MB。";
        return null;
    }

    /// <summary>运行 ConversationAgent，并把可见文字增量合并为 iMessage 回复。</summary>
    private async Task<IMessageInboundResponse> BuildReplyAsync(
        long userId,
        long turnId,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        await foreach (var update in conversations.RespondAsync(userId, turnId, cancellationToken))
        {
            if (update.Kind == "text_delta" && update.Text is not null)
                text.Append(update.Text);
            if (update.Kind == "failed")
                return new IMessageInboundResponse(
                    "reply",
                    update.ErrorMessage ?? "暂时没有回应成功，请稍后再试。");
        }
        return text.Length == 0
            ? new IMessageInboundResponse("reply", "暂时没有生成回复，请稍后再试。")
            : new IMessageInboundResponse("reply", text.ToString());
    }

    /// <summary>异常或重复投递时清理本次尚未绑定的图片。</summary>
    private async Task DeleteUploadedAsync(
        long userId,
        IEnumerable<long> attachmentIds,
        CancellationToken cancellationToken)
    {
        foreach (var attachmentId in attachmentIds)
            await attachments.DeleteUnboundAsync(userId, attachmentId, cancellationToken);
    }

    /// <summary>统一验证 Spectrum 网关共享密钥。</summary>
    private bool IsGatewayAuthorized()
    {
        var authorization = Request.Headers.Authorization.ToString();
        var suppliedSecret = authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            ? authorization["Bearer ".Length..]
            : null;
        return bindings.IsGatewayAuthorized(suppliedSecret);
    }

    /// <summary>创建不暴露内部异常的标准错误。</summary>
    private ProblemDetails CreateProblem(string title, int status) => new()
    {
        Title = title,
        Status = status,
        Instance = Request.Path,
    };
}
