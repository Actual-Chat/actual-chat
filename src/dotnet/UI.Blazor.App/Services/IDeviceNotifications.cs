namespace ActualChat.UI.Blazor.App.Services;

// Per-platform access to the device's OS-level notifications, used by NotificationReconciler to
// keep the device in sync with the server's active set. Registered only on platforms that own a
// notification tray (web SW, Android, iOS); absent elsewhere (the reconciler then no-ops).
public interface IDeviceNotifications
{
    // Reconciles the device's shown notifications against the server's active set: removes any
    // shown notification whose tag is not in `active`. (Creating missing ones is a later phase;
    // `active` already carries the content it will need.)
    Task Reconcile(IReadOnlyList<ActiveNotificationInfo> active, CancellationToken cancellationToken);
}

public sealed record ActiveNotificationInfo(
    string Tag,
    string Title,
    string Text,
    string IconUrl,
    string Url);
