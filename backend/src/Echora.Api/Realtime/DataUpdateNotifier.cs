using Microsoft.AspNetCore.SignalR;

namespace Echora.Api.Realtime;

/// <summary>供后台任务在状态变化后通知在线页面按需刷新。</summary>
public sealed class DataUpdateNotifier(IHubContext<DataUpdatesHub> hub)
{
    /// <summary>发送不含隐私内容的失效通知。</summary>
    public Task NotifyAsync(long userId, string state, CancellationToken cancellationToken, params string[] areas) =>
        hub.Clients
            .Group(DataUpdatesHub.UserGroup(userId))
            .SendAsync("dataChanged", new DataChangedMessage(state, areas), cancellationToken);

    /// <summary>前端只需要状态和受影响区域。</summary>
    public sealed record DataChangedMessage(string State, IReadOnlyList<string> Areas);
}
