using Echora.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Echora.Api.Realtime;

/// <summary>只通知当前用户哪些页面数据已经变化，不传输业务正文。</summary>
[Authorize]
public sealed class DataUpdatesHub : Hub
{
    /// <summary>连接建立后加入当前用户专属组。</summary>
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(Context.User!.GetRequiredId()));
        await base.OnConnectedAsync();
    }

    /// <summary>生成不会与其他用户重叠的组名。</summary>
    public static string UserGroup(long userId) => $"user:{userId}";
}
