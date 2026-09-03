using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

/// <summary>
/// AppKit twin of <see cref="MauiLocationPermissionHandler"/> on <see cref="MacOSPermissions"/>.
/// </summary>
public class MacOSLocationPermissionHandler : LocationPermissionHandler
{
    public MacOSLocationPermissionHandler(UIHub hub, bool mustStart = true)
        : base(hub, false)
    {
        ExpirationPeriod = null;
        if (mustStart)
            this.Start();
    }

    protected override async Task<bool?> Get(CancellationToken cancellationToken)
    {
        var isGranted = await MacOSPermissions.IsLocationGranted().ConfigureAwait(false);
        Log.LogInformation("Get: {IsGranted}", isGranted);
        return isGranted;
    }

    protected override Task<bool> Request(CancellationToken cancellationToken)
        => MacOSPermissions.RequestLocation(cancellationToken);

    protected override async Task Troubleshoot(CancellationToken cancellationToken)
    {
        var model = new LocationTroubleshooterModal.Model();
        var modalRef = await ModalUI.Show(model, cancellationToken).ConfigureAwait(true);
        await modalRef.WhenClosed.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
