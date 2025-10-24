using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class WebMicrophonePermissionHandler : MicrophonePermissionHandler
{
    private static readonly string JSCheckPermission = $"{BlazorUIAppModule.ImportName}.{nameof(WebMicrophonePermissionHandler)}.checkPermission";
    private static readonly string JSRequestPermission = $"{BlazorUIAppModule.ImportName}.{nameof(WebMicrophonePermissionHandler)}.requestPermission";

    [field: AllowNull, MaybeNull]
    protected AudioRecorder AudioRecorder => field ??= Hub.Services.GetRequiredService<AudioRecorder>();

    public WebMicrophonePermissionHandler(UIHub hub, bool mustStart = true) : base(hub, false)
    {
        // We don't need an expiration period - AudioRecorder is able to reset cached permission in case of recording failure
        ExpirationPeriod = null;
        if (mustStart)
            this.Start();
    }

    protected override async Task<bool?> Get(CancellationToken cancellationToken)
    {
        var js = Hub.JS;
        var permission = await js.InvokeAsync<string?>(JSCheckPermission, cancellationToken).ConfigureAwait(false);
        return permission switch {
            "prompt" => null,
            "denied" => false,
            "granted" => true,
            _ => null,
        };
    }

    protected override async Task<bool> Request(CancellationToken cancellationToken)
        => await Hub.JS.InvokeAsync<bool>(JSRequestPermission, cancellationToken).ConfigureAwait(false);

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
