using ActualChat.UI.Blazor.App.Module;
using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor.App.Services;

// Web IDeviceNotifications: forwards the active set to the service worker, which owns the shown
// notifications, so it can close the ones that are no longer active.
public class WebDeviceNotifications(AppUIHub hub) : IDeviceNotifications
{
    private static readonly string JSReconcileMethod =
        $"{BlazorUIAppModule.ImportName}.NotificationUI.reconcileNotifications";

    private IJSRuntime JS => hub.JS;

    public async Task Reconcile(
        IReadOnlyList<ActiveNotificationInfo> active,
        IReadOnlyCollection<string> createTags,
        CancellationToken cancellationToken)
        => await JS.InvokeVoidAsync(JSReconcileMethod, cancellationToken, active, createTags).ConfigureAwait(false);
}
