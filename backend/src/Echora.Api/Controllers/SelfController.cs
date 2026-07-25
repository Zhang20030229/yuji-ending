using Echora.Api.Services;
using Echora.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Echora.Api.Controllers;

/// <summary>提供 0.3 遇己的认识与情绪日/月接口。</summary>
[Authorize]
[ApiController]
[Route("api/self")]
public sealed class SelfController(SelfViewService self) : ControllerBase
{
    /// <summary>按分类或关键词读取认识。</summary>
    [HttpGet("recognitions")]
    public async Task<IActionResult> GetRecognitions(
        string? category,
        string? q,
        CancellationToken cancellationToken) =>
        Ok(await self.GetRecognitionsAsync(category, q, cancellationToken));

    /// <summary>读取指定日期的情绪。</summary>
    [HttpGet("emotions/day")]
    public async Task<IActionResult> GetEmotionDay(DateOnly? date, CancellationToken cancellationToken) =>
        Ok(await self.GetEmotionDayAsync(date ?? DateOnly.FromDateTime(DateTime.Today), cancellationToken));

    /// <summary>读取指定月份的情绪。</summary>
    [HttpGet("emotions/month")]
    public async Task<IActionResult> GetEmotionMonth(string? month, CancellationToken cancellationToken)
    {
        var value = DateOnly.FromDateTime(DateTime.Today);
        if (!string.IsNullOrWhiteSpace(month)
            && !DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", out value))
            return BadRequest("月份格式应为 yyyy-MM。");
        return Ok(await self.GetEmotionMonthAsync(value, cancellationToken));
    }

    /// <summary>读取指定年份的情绪概览。</summary>
    [HttpGet("emotions/year")]
    public async Task<IActionResult> GetEmotionYear(int? year, CancellationToken cancellationToken)
    {
        var value = year ?? DateTime.Today.Year;
        if (value is < 1900 or > 9999) return BadRequest("年份无效。");
        return Ok(await self.GetEmotionYearAsync(value, cancellationToken));
    }

    /// <summary>修订一条 CBT 自我观察，保留原消息不变。</summary>
    [HttpPatch("cbt-observations/{id:long}")]
    public async Task<IActionResult> UpdateCbtObservation(
        long id,
        UpdateCbtObservationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await self.UpdateCbtObservationAsync(id, request, cancellationToken)
                ? NoContent()
                : NotFound();
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

    /// <summary>删除一条 CBT 自我观察，保留原消息和情绪不变。</summary>
    [HttpDelete("cbt-observations/{id:long}")]
    public async Task<IActionResult> DeleteCbtObservation(long id, CancellationToken cancellationToken) =>
        await self.DeleteCbtObservationAsync(id, cancellationToken)
            ? NoContent()
            : NotFound();
}
