using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Echora.Api.Authentication;
using Echora.Api.Contracts;
using Echora.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Echora.Api.Controllers;

/// <summary>管理产品会话、可靠保存消息并流式返回 ConversationAgent 回答。</summary>
[Authorize]
[ApiController]
[Route("api/conversation")]
public sealed class ConversationController(
    ConversationService conversations,
    ConversationDeletionService deletions,
    IOptions<JsonOptions> apiJsonOptions) : ControllerBase
{
    /// <summary>读取最近活动的全部会话。</summary>
    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<ConversationSessionResponse>>> GetSessions(
        CancellationToken cancellationToken) =>
        Ok(await conversations.GetSessionsAsync(User.GetRequiredId(), cancellationToken));

    /// <summary>归档当前会话并创建新的可写会话。</summary>
    [HttpPost("sessions")]
    public async Task<ActionResult<ConversationSessionResponse>> CreateSession(
        CreateConversationRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await conversations.CreateSessionAsync(
                User.GetRequiredId(),
                request ?? new(),
                cancellationToken);
            return Created($"/api/conversation/sessions/{created.Id}", created);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>修改会话标题并阻止后台分析覆盖。</summary>
    [HttpPatch("sessions/{conversationId:long}")]
    public async Task<ActionResult<ConversationSessionResponse>> RenameSession(
        long conversationId,
        RenameConversationSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var changed = await conversations.RenameSessionAsync(
                User.GetRequiredId(),
                conversationId,
                request.Title,
                cancellationToken);
            return changed is null ? NotFound() : Ok(changed);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>读取删除会话将影响的全部整理结果数量。</summary>
    [HttpGet("sessions/{conversationId:long}/deletion-impact")]
    public async Task<IActionResult> GetDeletionImpact(long conversationId, CancellationToken cancellationToken)
    {
        var impact = await deletions.GetImpactAsync(User.GetRequiredId(), conversationId, cancellationToken);
        return impact is null ? NotFound() : Ok(impact);
    }

    /// <summary>删除会话和全部会话级整理结果；必要时自动建立新的当前会话。</summary>
    [HttpDelete("sessions/{conversationId:long}")]
    public async Task<IActionResult> DeleteSession(long conversationId, CancellationToken cancellationToken) =>
        await deletions.DeleteAsync(User.GetRequiredId(), conversationId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>按 Sequence 游标读取一页可见聊天记录。</summary>
    [HttpGet("sessions/{conversationId:long}/turns")]
    public async Task<ActionResult<ConversationTurnPage>> GetTurns(
        long conversationId,
        [FromQuery, Range(1, 100)] int limit = 20,
        [FromQuery] int? beforeSequence = null,
        CancellationToken cancellationToken = default) =>
        Ok(await conversations.GetTurnsAsync(
            User.GetRequiredId(),
            conversationId,
            limit,
            beforeSequence,
            cancellationToken));

    /// <summary>先保存 User Message；模型调用由后续流式接口启动。</summary>
    [HttpPost("sessions/{conversationId:long}/turns")]
    public async Task<ActionResult<TurnReceipt>> Capture(
        long conversationId,
        CaptureTurnRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clientMessageId = Request.Headers["Idempotency-Key"].ToString();
            var receipt = await conversations.CaptureAsync(
                User.GetRequiredId(),
                conversationId,
                clientMessageId,
                request,
                cancellationToken);
            return receipt.IsDuplicate
                ? Ok(receipt)
                : Created($"/api/conversation/turns/{receipt.TurnId}", receipt);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(exception.Message);
        }
    }

    /// <summary>执行 MAF 原生 Tool 循环，并把可见回答写成 SSE。</summary>
    [HttpPost("turns/{userMessageId:long}/responses")]
    public async Task Respond(long userMessageId, CancellationToken cancellationToken)
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Append("X-Accel-Buffering", "no");
        await foreach (var item in conversations.RespondAsync(
                           User.GetRequiredId(),
                           userMessageId,
                           cancellationToken))
        {
            var json = JsonSerializer.Serialize(item, apiJsonOptions.Value.JsonSerializerOptions);
            await Response.WriteAsync($"event: {item.Kind.Replace('_', '-')}\ndata: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}
