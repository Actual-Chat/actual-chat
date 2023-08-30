using ActualChat.Permissions;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Audio.UI.Blazor.Components;

public class WebMicrophonePermissionHandler : MicrophonePermissionHandler
{
    private AudioRecorder? _audioRecorder;
    private Dispatcher? _dispatcher;
    private ModalUI? _modalUI;

    protected AudioRecorder AudioRecorder => _audioRecorder ??= Services.GetRequiredService<AudioRecorder>();
    protected Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();
    protected ModalUI ModalUI => _modalUI ??= Services.GetRequiredService<ModalUI>();

    public WebMicrophonePermissionHandler(IServiceProvider services, bool mustStart = true)
        : base(services, mustStart)
        => ExpirationPeriod = null; // We don't need expiration period - AudioRecorder is able to reset cached permission in case of recording failure

    protected override Task<bool?> Get(CancellationToken cancellationToken)
        => AudioRecorder.CheckPermission(cancellationToken);

    protected override async Task<bool> Request(CancellationToken cancellationToken)
        => await AudioRecorder.RequestPermission(cancellationToken);

    protected override Task<bool> Troubleshoot(CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(async () => {
            var model = new GuideModal.Model(false, GuideType.WebChrome);
            var modalRef = await ModalUI.Show(model);
            try {
                await modalRef.WhenClosed.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) {
                modalRef.Close(true);
                return false;
            }
            return model.WasPermissionRequested;
        });
}
