using Echora.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Echora.Api.Controllers;

/// <summary>提供 0.3 拾光五个页面所需的直接业务接口。</summary>
[Authorize]
[ApiController]
[Route("api/archive")]
public sealed class ArchiveController(ArchiveViewService archive) : ControllerBase
{
    /// <summary>读取片段列表。</summary>
    [HttpGet("fragments")]
    public async Task<IActionResult> GetFragments(string? q, CancellationToken cancellationToken) =>
        Ok(await archive.GetFragmentsAsync(q, cancellationToken));

    /// <summary>读取片段详情。</summary>
    [HttpGet("fragments/{id:long}")]
    public async Task<IActionResult> GetFragment(long id, CancellationToken cancellationToken)
    {
        var item = await archive.GetFragmentAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>读取人物列表。</summary>
    [HttpGet("people")]
    public async Task<IActionResult> GetPeople(string? q, CancellationToken cancellationToken) =>
        Ok(await archive.GetPeopleAsync(q, cancellationToken));

    /// <summary>读取人物详情。</summary>
    [HttpGet("people/{id:long}")]
    public async Task<IActionResult> GetPerson(long id, CancellationToken cancellationToken)
    {
        var item = await archive.GetPersonAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>读取地点列表。</summary>
    [HttpGet("places")]
    public async Task<IActionResult> GetPlaces(string? q, CancellationToken cancellationToken) =>
        Ok(await archive.GetPlacesAsync(q, cancellationToken));

    /// <summary>读取地点详情。</summary>
    [HttpGet("places/{id:long}")]
    public async Task<IActionResult> GetPlace(long id, CancellationToken cancellationToken)
    {
        var item = await archive.GetPlaceAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>读取事件列表。</summary>
    [HttpGet("events")]
    public async Task<IActionResult> GetEvents(string? q, CancellationToken cancellationToken) =>
        Ok(await archive.GetEventsAsync(q, cancellationToken));

    /// <summary>读取事件详情。</summary>
    [HttpGet("events/{id:long}")]
    public async Task<IActionResult> GetEvent(long id, CancellationToken cancellationToken)
    {
        var item = await archive.GetEventAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>修改一条人物记录的 AI 总结。</summary>
    [HttpPut("people/records/{id:long}/summary")]
    public async Task<IActionResult> UpdatePersonRecordSummary(
        long id,
        UpdateSummaryRequest request,
        CancellationToken cancellationToken) =>
        await UpdateSummaryAsync(() => archive.UpdatePersonRecordSummaryAsync(id, request.Summary, cancellationToken));

    /// <summary>修改一条地点记录的 AI 总结。</summary>
    [HttpPut("places/records/{id:long}/summary")]
    public async Task<IActionResult> UpdatePlaceRecordSummary(
        long id,
        UpdateSummaryRequest request,
        CancellationToken cancellationToken) =>
        await UpdateSummaryAsync(() => archive.UpdatePlaceRecordSummaryAsync(id, request.Summary, cancellationToken));

    /// <summary>修改事件的 AI 总结。</summary>
    [HttpPut("events/{id:long}/summary")]
    public async Task<IActionResult> UpdateEventSummary(
        long id,
        UpdateSummaryRequest request,
        CancellationToken cancellationToken) =>
        await UpdateSummaryAsync(() => archive.UpdateEventSummaryAsync(id, request.Summary, cancellationToken));

    /// <summary>删除一条事件；前端必须先进行二次确认。</summary>
    [HttpDelete("events/{id:long}")]
    public async Task<IActionResult> DeleteEvent(long id, CancellationToken cancellationToken) =>
        await archive.DeleteEventAsync(id, cancellationToken) > 0 ? NoContent() : NotFound();

    /// <summary>读取全部待确认人物和地点。</summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending(CancellationToken cancellationToken) =>
        Ok(await archive.GetPendingAsync(cancellationToken));

    /// <summary>解决或忽略一条待确认项。</summary>
    [HttpPost("pending/{id:long}/resolve")]
    public async Task<IActionResult> ResolvePending(
        long id,
        ResolvePendingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await archive.ResolvePendingAsync(id, request.EntityId, request.NewName, request.Ignore, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>待确认项的选择、新建或忽略操作。</summary>
    public sealed record ResolvePendingRequest(long? EntityId, string? NewName, bool Ignore);

    /// <summary>用户人工修改后的总结。</summary>
    public sealed record UpdateSummaryRequest(string Summary);

    /// <summary>把三种总结更新映射为一致的 API 响应。</summary>
    private static async Task<IActionResult> UpdateSummaryAsync(Func<Task<bool>> update)
    {
        try
        {
            return await update() ? new NoContentResult() : new NotFoundResult();
        }
        catch (ArgumentException exception)
        {
            return new BadRequestObjectResult(exception.Message);
        }
    }
}
