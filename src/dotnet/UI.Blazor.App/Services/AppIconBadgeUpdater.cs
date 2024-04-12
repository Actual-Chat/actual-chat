using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public interface IAppIconBadge
{
    void SetUnreadChatCount(int count);
}

public class AppIconBadgeUpdater(ChatUIHub hub) : ScopedWorkerBase<ChatUIHub>(hub)
{
    protected override Task OnRun(CancellationToken cancellationToken)
        => Task.WhenAll(
            UpdateAppIconBadge(cancellationToken),
            Task.CompletedTask); // Just to add more items w/o need to worry about comma :)

    private async Task UpdateAppIconBadge(CancellationToken cancellationToken)
    {
        var badge = Services.GetService<IAppIconBadge>();
        if (badge is null)
            return;

        var lastUnreadCount = -1;
        var chatListUI = Hub.ChatListUI;
        var cUnreadCount0 = await Computed
            .Capture(() => chatListUI.UnreadChatCount.Use(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cUnreadCount0.Changes(cancellationToken);
        await foreach (var cUnreadCount in changes.ConfigureAwait(false)) {
            if (cUnreadCount.HasError)
                continue;

            var unreadCount = cUnreadCount.Value.Value;
            if (unreadCount == lastUnreadCount)
                continue;

            badge.SetUnreadChatCount(unreadCount);
            lastUnreadCount = unreadCount;
        }
    }
}
