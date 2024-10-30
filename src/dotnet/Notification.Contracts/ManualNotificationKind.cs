namespace ActualChat.Notification;

public enum ManualNotificationKind
{
    None = 0,
    NotifyMentionedMembers,
    //NotifyAll, just an example
    Invalid, // Must be the very last entry here - it is used in NotificationId parsing logic
}
