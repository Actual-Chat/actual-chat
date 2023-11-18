namespace ActualChat;

public enum NotificationKind
{
    None = 0,
    Message,
    Reply,
    Invitation,
    Mention,
    Reaction,
    Invalid, // Must be the very last entry here - it is used in NotificationId parsing logic
}
