using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Hosting;
using ActualChat.Permissions;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui.Services;

public class MauiMicrophonePermissionHandler : MicrophonePermissionHandler
{
    private ModalUI? _modalUI;

    protected ModalUI ModalUI => _modalUI ??= Services.GetRequiredService<ModalUI>();

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiMicrophonePermissionHandler))]
    public MauiMicrophonePermissionHandler(UIHub hub, bool mustStart = true)
        : base(hub, false)
    {
        // We don't need expiration period - AudioRecorder is able to reset cached permission in case of recording failure
        ExpirationPeriod = null;
        if (mustStart)
            this.Start();
    }

    protected override async Task<bool?> Get(CancellationToken cancellationToken)
    {
        var status = await MauiPermissions.CheckStatusAsync<MauiPermissions.Microphone>().ConfigureAwait(true);
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
