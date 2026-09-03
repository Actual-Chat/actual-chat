using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using AVFoundation;

namespace ActualChat.App.Maui;

// TODO(maui-labs): see MacOSMediaCapture
/// <summary>
/// AppKit twin of <see cref="MauiCameraPermissionHandler"/> on <see cref="MacOSMediaCapture"/>.
/// </summary>
public sealed class MacOSCameraPermissionHandler : CameraPermissionHandler
{
    public MacOSCameraPermissionHandler(UIHub hub, bool mustStart = true)
        : base(hub, false)
    {
        ExpirationPeriod = null;
        if (mustStart)
            this.Start();
    }

    protected override Task<bool?> Get(CancellationToken cancellationToken)
    {
        var isGranted = MacOSMediaCapture.IsGranted(AVAuthorizationMediaType.Video);
        Log.LogInformation("Get: {IsGranted}", isGranted);
        return Task.FromResult(isGranted);
    }

    protected override Task<bool> Request(CancellationToken cancellationToken)
        => MacOSMediaCapture.Request(AVAuthorizationMediaType.Video);

    protected override Task Troubleshoot(CancellationToken cancellationToken)
        => OpenSystemSettings();
}
