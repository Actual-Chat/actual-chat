using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Services;
using CoreLocation;

namespace ActualChat.App.Maui;

// TODO(maui-labs): see MacOSMediaCapture
/// <summary>
/// AppKit twin of <see cref="MauiLocationPermissionHandler"/> on CLLocationManager.
/// </summary>
public sealed class MacOSLocationPermissionHandler : LocationPermissionHandler
{
    private CLLocationManager LocationManager
        // CLLocationManager wants a thread with a run loop, so every access to it goes through the main thread
        => field ??= new CLLocationManager();

    public MacOSLocationPermissionHandler(UIHub hub, bool mustStart = true)
        : base(hub, false)
    {
        ExpirationPeriod = null;
        if (mustStart)
            this.Start();
    }

    protected override async Task<bool?> Get(CancellationToken cancellationToken)
    {
        var isGranted = await MacOSMainThread
            .InvokeOnMainThreadAsync(() => ToIsGranted(LocationManager.AuthorizationStatus))
            .ConfigureAwait(false);
        Log.LogInformation("Get: {IsGranted}", isGranted);
        return isGranted;
    }

    protected override Task<bool> Request(CancellationToken cancellationToken)
        => MacOSMainThread.InvokeOnMainThreadAsync(async () => {
            var manager = LocationManager;
            if (ToIsGranted(manager.AuthorizationStatus) is { } isGranted)
                return isGranted;

            // The prompt's outcome arrives through the delegate, never as a return value.
            // It has to be DidChangeAuthorization: the binding's delegate implements both callbacks,
            // and CoreLocation never calls the legacy one (AuthorizationChanged) when the new one exists.
            var whenDecided = TaskCompletionSourceExt.New<bool>();
            manager.DidChangeAuthorization += OnDidChangeAuthorization;
            try {
                manager.RequestWhenInUseAuthorization();
                return await whenDecided.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            }
            finally {
                manager.DidChangeAuthorization -= OnDidChangeAuthorization;
            }

            void OnDidChangeAuthorization(object? sender, EventArgs e) {
                if (ToIsGranted(manager.AuthorizationStatus) is { } isDecided)
                    whenDecided.TrySetResult(isDecided);
            }
        });

    protected override async Task Troubleshoot(CancellationToken cancellationToken)
    {
        var model = new LocationTroubleshooterModal.Model();
        var modalRef = await ModalUI.Show(model, cancellationToken).ConfigureAwait(true);
        await modalRef.WhenClosed.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private static bool? ToIsGranted(CLAuthorizationStatus status)
        => status switch {
            CLAuthorizationStatus.NotDetermined => null,
            CLAuthorizationStatus.Authorized => true,
            CLAuthorizationStatus.AuthorizedWhenInUse => true,
            _ => false,
        };
}
