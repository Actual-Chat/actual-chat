using ActualChat.UI.Blazor.App.Services;
using UserNotifications;

namespace ActualChat.App.Maui;

// iOS IDeviceNotifications: PRUNE-ONLY. Removes delivered notifications whose thread (= chat tag)
// is no longer active. Only touches notifications that carry a thread id (i.e. ours).
//
// createTags is intentionally ignored: iOS gives no user-dismissal callback and renders alert
// pushes without running our code, so we can't tell a dropped push from a banner the user swiped
// away — create-missing would resurrect dismissed notifications. Enabling it safely would require
// a Notification Service Extension recording delivered tags in an App Group (see
// docs/plans/notif-api.md "iOS create-missing / NSE"); revisit if/when we add an NSE for rich or
// E2E-encrypted notifications.
public class IosDeviceNotifications : IDeviceNotifications
{
    public Task Reconcile(
        IReadOnlyList<ActiveNotificationInfo> active,
        IReadOnlyCollection<string> createTags,
        CancellationToken cancellationToken)
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
