using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of <see cref="CameraPermissionHandler"/> using platform permission APIs.
/// </summary>
public class MauiCameraPermissionHandler : CameraPermissionHandler
{
    public MauiCameraPermissionHandler(UIHub hub, bool mustStart = true)
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
            status = await MauiPermissions.CheckStatusAsync<MauiPermissions.Camera>().ConfigureAwait(true);
        }
        catch (FileNotFoundException) {
            Log.LogWarning("Get: AppxManifest.xml not found, assuming camera permission is granted (unpackaged mode)");
            return true;
        }
        Log.LogInformation("Get: CheckStatusAsync<MauiPermissions.Camera>() response: {Status}", status);
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
        var status = await MauiPermissions.RequestAsync<MauiPermissions.Camera>().ConfigureAwait(true);
        return status is PermissionStatus.Granted or PermissionStatus.Limited;
    }

    protected override async Task Troubleshoot(CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        AppInfo.ShowSettingsUI();
    }
}
