namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661
public enum ExplicitNotificationKind
{
    None = 0,
    NotifyMentionedMembers,
    //NotifyMembers, just an example
    Invalid, // Must be the very last entry here - it is used in NotificationId parsing logic
}
