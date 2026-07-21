using AVFoundation;

namespace ActualChat.App.Maui.Video;

internal static class AppleVideoDevices
{
    private static readonly AVCaptureDeviceType[] DeviceTypes = [
        AVCaptureDeviceType.BuiltInWideAngleCamera,
        AVCaptureDeviceType.External,
    ];

    public static AVCaptureDevice[] All()
    {
        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        if (status == AVAuthorizationStatus.NotDetermined)
            AVCaptureDevice.RequestAccessForMediaType(AVAuthorizationMediaType.Video, _ => { });

        var discovery = AVCaptureDeviceDiscoverySession.Create(
            DeviceTypes, AVMediaTypes.Video, AVCaptureDevicePosition.Unspecified);
        return discovery.Devices;
    }
}
