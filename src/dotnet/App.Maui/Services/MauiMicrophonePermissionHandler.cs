using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Hosting;
using ActualChat.Permissions;
using ActualChat.UI.Blazor.Services;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;
using Dispatcher = Microsoft.AspNetCore.Components.Dispatcher;

namespace ActualChat.App.Maui.Services;

public class MauiMicrophonePermissionHandler : MicrophonePermissionHandler
{
    private Dispatcher? _dispatcher;
    private ModalUI? _modalUI;
    private HostInfo? _hostInfo;

    protected Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();
    protected ModalUI ModalUI => _modalUI ??= Services.GetRequiredService<ModalUI>();
    protected HostInfo HostInfo => _hostInfo ??= Services.GetRequiredService<HostInfo>();

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
        var status = await MauiPermissions
            .RequestAsync<MauiPermissions.Microphone>()
            .ConfigureAwait(true);
       return status is PermissionStatus.Granted or PermissionStatus.Limited;
    }

    protected override Task<bool> Troubleshoot(CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(async () => {
            var model = new GuideModal.Model(false, GuideType.WebChrome);
            var modalRef = await ModalUI.Show(model, cancellationToken);
            try {
                await modalRef.WhenClosed.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) {
                return false;
            }
            return model.WasPermissionRequested;
        });
}
