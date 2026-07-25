using Echora.Api.Authentication;
using Echora.Api.Contracts;
using Echora.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Echora.Api.Controllers;

/// <summary>提供当前用户的私有图片上传、读取和未绑定删除 API。</summary>
[ApiController]
[Authorize]
[Route("api/assets")]
public sealed class AssetsController(AttachmentService attachments) : ControllerBase
{
    /// <summary>上传一张 JPEG、PNG、WebP、HEIC 或 HEIF 图片。</summary>
    [HttpPost]
    [RequestSizeLimit(AttachmentService.MaxBytes)]
    public async Task<ActionResult<AttachmentResponse>> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await attachments.UploadAsync(
                User.GetRequiredId(),
                file,
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Title = exception.Message,
                Status = StatusCodes.Status400BadRequest,
                Instance = Request.Path,
            });
        }
    }

    /// <summary>读取当前用户的一张私有图片。</summary>
    [HttpGet("{id:long}/content")]
    public async Task<IActionResult> Content(long id, CancellationToken cancellationToken)
    {
        var result = await attachments.ReadDisplayAsync(User.GetRequiredId(), id, cancellationToken);
        return result is null
            ? NotFound()
            : File(result.Value.Bytes, result.Value.MimeType);
    }

    /// <summary>读取用于列表和地图的 WebP 缩略图。</summary>
    [HttpGet("{id:long}/thumbnail")]
    public async Task<IActionResult> Thumbnail(
        long id,
        [FromQuery] int size = 320,
        [FromQuery] int quality = 76,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await attachments.ReadThumbnailAsync(
                User.GetRequiredId(),
                id,
                size,
                quality,
                cancellationToken);
            if (result is null) return NotFound();
            Response.Headers.CacheControl = "private,max-age=604800";
            return File(result.Value.Bytes, "image/webp");
        }
        catch (ArgumentOutOfRangeException)
        {
            return BadRequest("缩略图尺寸或质量参数无效。");
        }
    }

    /// <summary>删除尚未进入聊天或一刻的图片。</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        return await attachments.DeleteUnboundAsync(User.GetRequiredId(), id, cancellationToken)
            ? NoContent()
            : Conflict(new ProblemDetails
            {
                Title = "已进入记录的图片不能单独删除。",
                Status = StatusCodes.Status409Conflict,
                Instance = Request.Path,
            });
    }
}
