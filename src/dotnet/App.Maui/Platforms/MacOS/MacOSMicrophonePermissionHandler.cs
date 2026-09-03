using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Services;
using AVFoundation;

namespace ActualChat.App.Maui;

/// <summary>
/// AppKit twin of <see cref="MauiMicrophonePermissionHandler"/> on <see cref="MacOSPermissions"/>.
/// </summary>
public class MacOSMicrophonePermissionHandler : MicrophonePermissionHandler
{
    public MacOSMicrophonePermissionHandler(UIHub hub, bool mustStart = true)
        : base(hub, false)
    {
        // No expiration period: AudioRecorder resets the cached permission when a recording fails
        ExpirationPeriod = null;
        if (mustStart)
            this.Start();
    }

    protected override Task<bool?> Get(CancellationToken cancellationToken)
    {
        var isGranted = MacOSPermissions.IsMediaCaptureGranted(AVAuthorizationMediaType.Audio);
        Log.LogInformation("Get: {IsGranted}", isGranted);
        return Task.FromResult(isGranted);
    }

    protected override Task<bool> Request(CancellationToken cancellationToken)
        => MacOSPermissions.RequestMediaCapture(AVAuthorizationMediaType.Audio);

    protected override async Task Troubleshoot(CancellationToken cancellationToken)
    {
        var model = new RecordingTroubleshooterModal.Model();
        var modalRef = await ModalUI.Show(model, cancellationToken).ConfigureAwait(true);
        await modalRef.WhenClosed.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
