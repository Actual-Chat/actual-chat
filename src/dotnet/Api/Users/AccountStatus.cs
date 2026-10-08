namespace ActualChat.Users;

/// <summary>
/// Specifies the status of a user account.
/// </summary>
public enum AccountStatus
{
    Active = 0,
    Inactive = 1,
    Suspended = 2,
    Removed = 3, // A tombstone: id stays taken so it can never be handed out again
}
