using Echora.Api.Services;
using Echora.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Echora.Api.Controllers;

/// <summary>为高频页面一次性组合展示数据，避免低带宽环境产生并发请求风暴。</summary>
[Authorize]
[ApiController]
[Route("api")]
public sealed class OverviewController(
    ArchiveViewService archive,
    SelfViewService self,
    HeartReportService reports,
    MomentService moments,
    RunLogService logs) : ControllerBase
{
    /// <summary>一次读取首页需要的事件、人物、地点、情绪、认识和入口计数。</summary>
    [HttpGet("home/overview")]
    public async Task<IActionResult> GetHome(CancellationToken cancellationToken)
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ShanghaiTimeZone);
        var today = DateOnly.FromDateTime(now.DateTime);
        var events = await archive.GetEventsAsync(null, cancellationToken);
        var people = await archive.GetPeopleAsync(null, cancellationToken);
        var places = await archive.GetPlacesAsync(null, cancellationToken);
        var emotion = await self.GetEmotionDayAsync(today, cancellationToken);
        var recognitions = await self.GetRecognitionsAsync(null, null, cancellationToken);
        var pending = await archive.GetPendingAsync(cancellationToken);
        var fragments = await archive.GetFragmentsAsync(null, cancellationToken);
        var reportItems = await reports.GetReportsAsync(cancellationToken);
        var status = await logs.GetStatusAsync(cancellationToken);
        return Ok(new
        {
            events,
            people,
            places,
            emotion,
            recognitions,
            pending,
            fragments,
            reports = reportItems,
            analysisStatus = status,
        });
    }

    /// <summary>一次读取地图的地点和事件。</summary>
    [HttpGet("archive/map/overview")]
    public async Task<IActionResult> GetMap(CancellationToken cancellationToken)
    {
        var places = await archive.GetPlacesAsync(null, cancellationToken);
        var events = await archive.GetEventsAsync(null, cancellationToken);
        return Ok(new { places, events });
    }

    /// <summary>一次读取已整理片段和一刻处理状态。</summary>
    [HttpGet("archive/fragments/overview")]
    public async Task<IActionResult> GetFragments(CancellationToken cancellationToken)
    {
        var fragments = await archive.GetFragmentsAsync(null, cancellationToken);
        var momentItems = await moments.GetAsync(User.GetRequiredId(), cancellationToken);
        return Ok(new { fragments, moments = momentItems });
    }

    /// <summary>一次读取待确认项及全部可选人物和地点。</summary>
    [HttpGet("archive/pending/overview")]
    public async Task<IActionResult> GetPending(CancellationToken cancellationToken)
    {
        var items = await archive.GetPendingAsync(cancellationToken);
        var people = await archive.GetPeopleAsync(null, cancellationToken);
        var places = await archive.GetPlacesAsync(null, cancellationToken);
        return Ok(new { items, people, places });
    }

    /// <summary>一次读取心迹报告和 WHO-5 自评历史。</summary>
    [HttpGet("reports/overview")]
    public async Task<IActionResult> GetReports(CancellationToken cancellationToken)
    {
        var reportItems = await reports.GetReportsAsync(cancellationToken);
        var wellbeing = await reports.GetWellbeingAsync(cancellationToken);
        return Ok(new { reports = reportItems, wellbeing });
    }

    private static readonly TimeZoneInfo ShanghaiTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
}
