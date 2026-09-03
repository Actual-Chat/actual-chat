using AVFoundation;

namespace ActualChat.App.Maui;

// TODO(maui-labs): delete, with the MacOS*PermissionHandler classes, once Essentials' Permissions
// is implemented on the macos TFM - the Maui* handlers then apply.
/// <summary>
/// The microphone / camera TCC state, read by both permission handlers and by the WebKit
/// media-capture delegate. Null means the user hasn't been asked yet.
/// </summary>
public static class MacOSMediaCapture
{
    public static bool? IsGranted(AVAuthorizationMediaType mediaType)
        => AVCaptureDevice.GetAuthorizationStatus(mediaType) switch {
            AVAuthorizationStatus.NotDetermined => null,
            AVAuthorizationStatus.Authorized => true,
            _ => false,
        };

    public static Task<bool> Request(AVAuthorizationMediaType mediaType)
        => AVCaptureDevice.RequestAccessForMediaTypeAsync(mediaType);
}
