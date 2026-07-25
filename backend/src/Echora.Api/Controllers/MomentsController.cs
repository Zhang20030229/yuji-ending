using Echora.Api.Authentication;
using Echora.Api.Contracts;
using Echora.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Echora.Api.Controllers;

/// <summary>提供一刻的发布、读取、重试和删除接口。</summary>
[Authorize]
[ApiController]
[Route("api/moments")]
public sealed class MomentsController(MomentService moments) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(await moments.GetAsync(User.GetRequiredId(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreateMomentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await moments.CreateAsync(User.GetRequiredId(), request, cancellationToken);
            return Created($"/api/moments/{result.Id}", result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost("{id:long}/retry")]
    public async Task<IActionResult> Retry(long id, CancellationToken cancellationToken) =>
        await moments.RetryAsync(User.GetRequiredId(), id, cancellationToken) ? Accepted() : NotFound();

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken) =>
        await moments.DeleteAsync(User.GetRequiredId(), id, cancellationToken) ? NoContent() : NotFound();
}
