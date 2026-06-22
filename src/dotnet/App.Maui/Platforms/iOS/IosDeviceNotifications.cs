using ActualChat.UI.Blazor.App.Services;
using UserNotifications;

namespace ActualChat.App.Maui;

// iOS IDeviceNotifications: removes delivered notifications whose thread (= chat tag) is no
// longer active. Only touches notifications that carry a thread id (i.e. ours).
public class IosDeviceNotifications : IDeviceNotifications
{
    public Task Reconcile(IReadOnlyList<ActiveNotificationInfo> active, CancellationToken cancellationToken)
    {
        var activeTags = active.Select(x => x.Tag).ToHashSet(StringComparer.Ordinal);
        var tcs = new TaskCompletionSource();
        UNUserNotificationCenter.Current.GetDeliveredNotifications(delivered => {
            try {
                var toRemove = delivered
                    .Where(n => {
                        var tag = n.Request.Content.ThreadIdentifier;
                        return !tag.IsNullOrEmpty() && !activeTags.Contains(tag);
                    })
                    .Select(n => n.Request.Identifier)
                    .ToArray();
                if (toRemove.Length > 0)
                    UNUserNotificationCenter.Current.RemoveDeliveredNotifications(toRemove);
            }
            finally {
                tcs.TrySetResult();
            }
        });
        return tcs.Task;
    }
}
