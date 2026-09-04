namespace ActualChat.Notifications;

/// <summary>
/// Identifies the type of client device receiving notifications.
/// </summary>
// ReSharper disable once InconsistentNaming
public enum DeviceType
{
    WebBrowser = 0,
    WindowsApp = 1,
    iOSApp = 2,
    AndroidApp = 3,
    // Apple Push to Talk token (ephemeral, from PTChannelManager) - direct APNs only,
    // must never be handed to FCM.
    iOSPttApp = 4,
    // Apple PushKit VoIP token for CallKit rings - direct APNs only, must never be
    // handed to FCM.
    iOSVoipApp = 5,
}

public static class DeviceTypeExt
{
    public static bool IsFcm(this DeviceType deviceType)
        // Allowlist: an unlisted type must default to "not FCM", because handing a direct-push
        // token to FCM gets the device row deleted and the user stops receiving calls.
        => deviceType is DeviceType.WebBrowser
            or DeviceType.WindowsApp
            or DeviceType.iOSApp
            or DeviceType.AndroidApp;
}
