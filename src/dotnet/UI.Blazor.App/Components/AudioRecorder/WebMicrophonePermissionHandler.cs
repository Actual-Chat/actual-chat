using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class WebMicrophonePermissionHandler : MicrophonePermissionHandler
{
    [field: AllowNull, MaybeNull]
    protected AudioRecorder AudioRecorder => field ??= Hub.Services.GetRequiredService<AudioRecorder>();

    public WebMicrophonePermissionHandler(UIHub hub, bool mustStart = true) : base(hub, false)
    {
        // We don't need an expiration period - AudioRecorder is able to reset cached permission in case of recording failure
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
        var modalRef = await ModalUI
            .Show(model, cancellationToken)
            .ConfigureAwait(false); // Ok (see the next line)
        await modalRef.WhenClosed
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false); // Ok (pre-exit)
    }
}
