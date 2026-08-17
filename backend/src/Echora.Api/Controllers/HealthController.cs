using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace Echora.Api.Controllers;

/// <summary>
/// 无需登录的存活检查，不读取任何用户数据。
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public sealed class HealthController(ISqlSugarClient db) : ControllerBase
{
    /// <summary>返回进程时间与数据库连通性，不含私有数据、版本或配置。</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        try
        {
            await db.Ado.GetScalarAsync("select 1", parameters: null, cancellationToken);
        }
        catch (Exception)
        {
            // 探活只暴露状态；异常细节留在服务端日志，避免向公网泄漏连接串或拓扑。
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "degraded", time = DateTime.UtcNow });
        }
        return Ok(new { status = "ok", time = DateTime.UtcNow });
    }
}
