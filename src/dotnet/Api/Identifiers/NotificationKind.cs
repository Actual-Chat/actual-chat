namespace ActualChat;

/// <summary>
/// Specifies the type of notification.
/// </summary>
public enum NotificationKind
{
    None = 0,
    Message,
    Reply,
    Invitation,
    Mention,
    Reaction,
    Attention,
    Thread,
    Conversation,
    IncomingCall,
    Invalid, // Must be the very last entry here - it is used in NotificationId parsing logic
}
