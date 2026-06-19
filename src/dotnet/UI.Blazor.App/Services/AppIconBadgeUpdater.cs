using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public interface IAppIconBadge
{
    void SetUnreadChatCount(int count);
}

public class AppIconBadgeUpdater(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub)
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

        var lastCount = -1;
        var notifications = Hub.Notifications;
        var session = Hub.Session;
        var cActive0 = await Computed
            .Capture(() => notifications.ListActive(session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cActive0.Changes(cancellationToken);
        await foreach (var cActive in changes.ConfigureAwait(false)) {
            if (cActive.HasError)
                continue;

            var count = cActive.Value.Count;
            if (count == lastCount)
                continue;

            badge.SetUnreadChatCount(count);
            lastCount = count;
        }
    }
}
