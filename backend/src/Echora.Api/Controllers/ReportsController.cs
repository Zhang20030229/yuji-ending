using Echora.Api.Contracts;
using Echora.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Echora.Api.Controllers;

/// <summary>提供心迹报告与 WHO-5 自评接口。</summary>
[Authorize]
[ApiController]
[Route("api/reports")]
public sealed class ReportsController(HeartReportService reports) : ControllerBase
{
    /// <summary>读取当前用户的报告历史。</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(await reports.GetReportsAsync(cancellationToken));

    /// <summary>读取一个报告包详情。</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetOne(long id, CancellationToken cancellationToken)
    {
        var item = await reports.GetReportAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>创建今天、最近 7 天、最近 30 天或自定义日期报告。</summary>
    [HttpPost]
    public async Task<IActionResult> Generate(
        GenerateReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await reports.GenerateAsync(request, cancellationToken));
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

    /// <summary>重试一个失败分区或综合心迹。</summary>
    [HttpPost("{id:long}/retry")]
    public async Task<IActionResult> Retry(
        long id,
        [FromQuery] string kind,
        CancellationToken cancellationToken)
    {
        try
        {
            return await reports.RetryAsync(id, kind, cancellationToken)
                ? NoContent()
                : Conflict(new ProblemDetails
                {
                    Title = "该部分当前不需要重试。",
                    Status = StatusCodes.Status409Conflict,
                    Instance = Request.Path,
                });
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

    /// <summary>读取最近 WHO-5 自评历史。</summary>
    [HttpGet("wellbeing")]
    public async Task<IActionResult> GetWellbeing(CancellationToken cancellationToken) =>
        Ok(await reports.GetWellbeingAsync(cancellationToken));

    /// <summary>保存完整五题并返回固定计分结果。</summary>
    [HttpPost("wellbeing")]
    public async Task<IActionResult> SaveWellbeing(
        WellbeingAssessmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await reports.SaveWellbeingAsync(request, cancellationToken));
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
}
