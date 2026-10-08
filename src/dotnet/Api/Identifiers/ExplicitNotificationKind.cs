namespace ActualChat;

/// <summary>
/// Specifies explicit notification targeting options.
/// </summary>
#pragma warning disable CS0659, CS0660, CS0661
public enum ExplicitNotificationKind
{
    None = 0,
    NotifyMentionedMembers,
    // Per-author granularity: one record per (entry, mentioned author), so we can tell who's been alerted
    NotifyMentionedMember,
    //NotifyMembers, just an example
    Invalid, // Must be the very last entry here - it is used in NotificationId parsing logic
}
