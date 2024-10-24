using ActualChat.Permissions;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class WebMicrophonePermissionHandler : MicrophonePermissionHandler
{
    private AudioRecorder? _audioRecorder;
    private ModalUI? _modalUI;

    protected AudioRecorder AudioRecorder => _audioRecorder ??= Services.GetRequiredService<AudioRecorder>();
    protected ModalUI ModalUI => _modalUI ??= Services.GetRequiredService<ModalUI>();

    public WebMicrophonePermissionHandler(UIHub hub, bool mustStart = true) : base(hub, false)
    {
        // We don't need expiration period - AudioRecorder is able to reset cached permission in case of recording failure
        ExpirationPeriod = null;
        if (mustStart)
            this.Start();
    }

    protected override Task<bool?> Get(CancellationToken cancellationToken)
        => AudioRecorder.CheckPermission(cancellationToken);

    protected override async Task<bool> Request(CancellationToken cancellationToken)
        => await AudioRecorder.RequestPermission(cancellationToken);

    protected override async Task Troubleshoot(CancellationToken cancellationToken)
    {
        var model = new RecordingTroubleshooterModal.Model();
        var modalRef = await ModalUI.Show(model, cancellationToken).ConfigureAwait(true);
        await modalRef.WhenClosed.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
