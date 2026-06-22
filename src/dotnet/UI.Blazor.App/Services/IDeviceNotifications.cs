namespace ActualChat.UI.Blazor.App.Services;

// Per-platform access to the device's OS-level notifications, used by NotificationReconciler to
// keep the device in sync with the server's active set. Registered only on platforms that own a
// notification tray (web SW, Android, iOS); absent elsewhere (the reconciler then no-ops).
public interface IDeviceNotifications
{
    // Reconciles the device's shown notifications against the server's active set:
    // - removes any shown notification whose tag is not in `active` (prune);
    // - creates a notification for each tag in `createTags` that isn't already shown, using the
    //   matching content from `active` (heals a dropped delivery push).
    // `createTags` carries only tags that newly entered the active set this tick, so dismissing a
    // banner (which doesn't change the active set) never re-creates it. iOS ignores createTags
    // (prune-only) — see the platform impl note.
    Task Reconcile(
        IReadOnlyList<ActiveNotificationInfo> active,
        IReadOnlyCollection<string> createTags,
        CancellationToken cancellationToken);
}

public sealed record ActiveNotificationInfo(
    string Tag,
    string Title,
    string Text,
    string IconUrl,
    string Url);
