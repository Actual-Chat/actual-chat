using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Hosting;
using ActualChat.Permissions;
using ActualChat.UI.Blazor.Services;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui.Services;

public class MauiMicrophonePermissionHandler : MicrophonePermissionHandler
{
    private ModalUI? _modalUI;

    protected ModalUI ModalUI => _modalUI ??= Services.GetRequiredService<ModalUI>();

    public MauiMicrophonePermissionHandler(IServiceProvider services, bool mustStart = true)
        : base(services, mustStart)
        => ExpirationPeriod = null; // We don't need expiration period - AudioRecorder is able to reset cached permission in case of recording failure

    protected override async Task<bool?> Get(CancellationToken cancellationToken)
    {
        var status = await MauiPermissions
            .CheckStatusAsync<MauiPermissions.Microphone>()
            .ConfigureAwait(true);
        // Android returns Denied when permission is not set, also you can request permissions again
        return status switch {
            PermissionStatus.Denied => HostInfo.ClientKind == ClientKind.Android ? null : false,
            PermissionStatus.Disabled => false,
            PermissionStatus.Restricted => false,
            PermissionStatus.Unknown => null,
            PermissionStatus.Granted => true,
            PermissionStatus.Limited => true,
            _ => throw StandardError.NotSupported<PermissionStatus>(status.ToString()),
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
