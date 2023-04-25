using ActualChat.Permissions;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Audio.UI.Blazor.Components;

public class MicrophonePermissionHandler : PermissionHandler
{
    private AudioRecorder? _audioRecorder;
    private Dispatcher? _dispatcher;
    private ModalUI? _modalUI;

    protected AudioRecorder AudioRecorder => _audioRecorder ??= Services.GetRequiredService<AudioRecorder>();
    protected Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();
    protected ModalUI ModalUI => _modalUI ??= Services.GetRequiredService<ModalUI>();

    public MicrophonePermissionHandler(IServiceProvider services, bool mustStart = true)
        : base(services, mustStart)
        => ExpirationPeriod = null; // We don't need expiration period - AudioRecorder is able to reset cached permission in case of recording failure

    protected override Task<bool> Check(CancellationToken cancellationToken)
        => AudioRecorder.RequestPermission(cancellationToken);

    protected override Task<bool> Request(CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(async () => {
            var model = new GuideModal.Model();
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
