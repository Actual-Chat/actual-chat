using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Services;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of <see cref="LocationPermissionHandler"/> using platform permission APIs.
/// </summary>
public class MauiLocationPermissionHandler : LocationPermissionHandler
{
    public MauiLocationPermissionHandler(UIHub hub, bool mustStart = true)
        : base(hub, false)
    {
        ExpirationPeriod = null;
        if (mustStart)
            this.Start();
    }

    protected override async Task<bool?> Get(CancellationToken cancellationToken)
    {
        PermissionStatus status;
        try {
            status = await MauiPermissions.CheckStatusAsync<MauiPermissions.LocationWhenInUse>().ConfigureAwait(true);
        }
        catch (FileNotFoundException) {
            Log.LogWarning("Get: AppxManifest.xml not found, assuming location permission is granted (unpackaged mode)");
            return true;
        }
        Log.LogInformation("Get: CheckStatusAsync<MauiPermissions.LocationWhenInUse>() response: {Status}", status);
        return status switch {
            PermissionStatus.Granted => true,
            PermissionStatus.Limited => true,
            PermissionStatus.Unknown => null,
            PermissionStatus.Denied => HostInfo.AppKind == AppKind.Android ? null : false,
            _ => false,
        };
    }

    protected override async Task<bool> Request(CancellationToken cancellationToken)
    {
        var status = await MauiPermissions.RequestAsync<MauiPermissions.LocationWhenInUse>().ConfigureAwait(true);
        return status is PermissionStatus.Granted or PermissionStatus.Limited;
    }

    protected override async Task Troubleshoot(CancellationToken cancellationToken)
    {
        var model = new LocationTroubleshooterModal.Model();
        var modalRef = await ModalUI.Show(model, cancellationToken).ConfigureAwait(true);
        await modalRef.WhenClosed.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
