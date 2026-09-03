using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using AVFoundation;

namespace ActualChat.App.Maui;

/// <summary>
/// AppKit twin of <see cref="MauiCameraPermissionHandler"/> on <see cref="MacOSPermissions"/>.
/// </summary>
public class MacOSCameraPermissionHandler : CameraPermissionHandler
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
        var isGranted = MacOSPermissions.IsMediaCaptureGranted(AVAuthorizationMediaType.Video);
        Log.LogInformation("Get: {IsGranted}", isGranted);
        return Task.FromResult(isGranted);
    }

    protected override Task<bool> Request(CancellationToken cancellationToken)
        => MacOSPermissions.RequestMediaCapture(AVAuthorizationMediaType.Video);

    protected override Task Troubleshoot(CancellationToken cancellationToken)
        => OpenSystemSettings();
}
