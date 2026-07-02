namespace ActualChat.Notifications;

/// <summary>
/// Delivery class used by per-chat notification-mode filtering: Ordinary is delivered only in
/// Default mode, Important also in ImportantOnly, Ringer always — explicit attention pings and
/// incoming calls break through mute.
/// </summary>
public enum NotificationImportance
{
    Ordinary = 0,
    Important = 1,
    Ringer = 2,
}
