using Android.App;
using AndroidX.Core.App;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

// Android IDeviceNotifications: closes shown notifications whose tag is no longer active.
public class AndroidDeviceNotifications : IDeviceNotifications
{
    public Task Reconcile(IReadOnlyList<ActiveNotificationInfo> active, CancellationToken cancellationToken)
    {
        var activeTags = active.Select(x => x.Tag).ToHashSet(StringComparer.Ordinal);
        var notificationManager = NotificationManagerCompat.From(Application.Context);
        var shown = notificationManager.ActiveNotifications;
        if (shown != null)
            foreach (var statusBarNotification in shown) {
                var tag = statusBarNotification.Tag;
                if (!tag.IsNullOrEmpty() && !activeTags.Contains(tag))
                    notificationManager.Cancel(tag, statusBarNotification.Id);
            }
        return Task.CompletedTask;
    }
}
