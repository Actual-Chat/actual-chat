using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Services;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of <see cref="MicrophonePermissionHandler"/> using platform permission APIs.
/// </summary>
public class MauiMicrophonePermissionHandler : MicrophonePermissionHandler
{
    public MauiMicrophonePermissionHandler(UIHub hub, bool mustStart = true)
        : base(hub, false)
    {
        // We don't need an expiration period - AudioRecorder is able to reset cached permission in case of recording failure
        ExpirationPeriod = null;
        if (mustStart)
            this.Start();
    }

    protected override async Task<bool?> Get(CancellationToken cancellationToken)
    {
        PermissionStatus status;
        try {
            status = await MauiPermissions.CheckStatusAsync<MauiPermissions.Microphone>().ConfigureAwait(true);
        }
        catch (FileNotFoundException) {
            // AppxManifest.xml is missing when running outside of MSIX package (e.g. published exe);
            // unpackaged Windows apps don't require capability declarations.
            Log.LogWarning("Get: AppxManifest.xml not found, assuming microphone permission is granted (unpackaged mode)");
            return true;
        }
        Log.LogInformation("Get: CheckStatusAsync<MauiPermissions.Microphone>() response: {Status}", status);
        // Android returns Denied when permission is not set, also you can request permissions again
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
        var status = await MauiPermissions.RequestAsync<MauiPermissions.Microphone>().ConfigureAwait(true);
       return status is PermissionStatus.Granted or PermissionStatus.Limited;
    }

    protected override async Task Troubleshoot(CancellationToken cancellationToken)
    {
        var model = new RecordingTroubleshooterModal.Model();
        var modalRef = await ModalUI.Show(model, cancellationToken).ConfigureAwait(true);
        await modalRef.WhenClosed.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
