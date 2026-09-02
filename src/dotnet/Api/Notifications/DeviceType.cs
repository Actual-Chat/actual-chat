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
    // The push-only types hold PushKit tokens, which FCM rejects - and a rejected token
    // is dropped from the whole batch, so this filters rather than the call sites.
    public static bool IsFcm(this DeviceType deviceType)
        => deviceType is not (DeviceType.iOSPttApp or DeviceType.iOSVoipApp);
}
