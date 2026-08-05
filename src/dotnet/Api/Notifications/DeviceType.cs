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
}
