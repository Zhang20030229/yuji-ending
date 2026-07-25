using Echora.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Echora.Api.Controllers;

/// <summary>读取和重试 AnalysisRun 失败分支。</summary>
[Authorize]
[ApiController]
[Route("api/run-logs")]
public sealed class RunLogsController(RunLogService logs) : ControllerBase
{
    /// <summary>读取拾光和遇己当前正在整理的分支数量。</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken) =>
        Ok(await logs.GetStatusAsync(cancellationToken));

    /// <summary>读取最近失败分支。</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(await logs.GetAsync(cancellationToken));

    /// <summary>重试当前版本中尚未成功的分支。</summary>
    [HttpPost("processing-runs/{id:long}/retry")]
    public async Task<IActionResult> Retry(long id, CancellationToken cancellationToken) =>
        await logs.RetryAsync(id, cancellationToken) ? NoContent() : Conflict("该运行已经过期或不存在。");
}
