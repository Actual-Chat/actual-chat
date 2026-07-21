using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Video;
using AVFoundation;

namespace ActualChat.App.Maui.Video;

public sealed class MauiCameraDevices : INativeCameraDevices
{
    public VideoDevice[] List()
    {
        var devices = AppleVideoDevices.All();
        return devices
            .Select(d => new VideoDevice(d.UniqueID, d.LocalizedName, ToFacing(d.Position)))
            .ToArray();
    }

    // Private methods

    private static string? ToFacing(AVCaptureDevicePosition position)
        => position switch {
            AVCaptureDevicePosition.Front => "user",
            AVCaptureDevicePosition.Back => "environment",
            _ => null,
        };
}
