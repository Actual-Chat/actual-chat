namespace ActualChat;

public enum NotificationKind
{
    Message = 1,
    Reply,
    Invitation,
    Mention,
    Reaction,
    Invalid, // Must be the very last entry here - it is used in NotificationId parsing logic
}
