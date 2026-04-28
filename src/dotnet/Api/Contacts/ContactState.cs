namespace ActualChat.Contacts;

#pragma warning disable CA1027

/// <summary>
/// Describes how a contact should participate in contact-list and peer-chat rules.
/// </summary>
public enum ContactState
{
    Temporary = 0,
    Regular = 0x10,
    // Archieved = 0x100,
    Blocked = 0x1000,
}
