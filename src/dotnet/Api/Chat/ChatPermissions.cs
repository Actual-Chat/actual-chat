namespace ActualChat.Chat;

/// <summary>
/// Defines permission flags for chat operations.
/// </summary>
#pragma warning disable MA0062
#pragma warning disable RCS1157

[Flags]
public enum ChatPermissions
{
    Read           = 0x1,
    Write          = 0x2, // Implies Read
    SeeMembers     = 0x4,

    Join           = 0x80, // Implies Read
    Invite         = 0x100, // Implies SeeMember, Join (-> Read)
    Leave          = 0x200,

    EditProperties = 0x1000,
    EditRoles      = 0x2000,
    EditMembers    = 0x4000,
    Moderate       = 0x8000, // Implies EditProperties, EditMembers (-> SeeMembers)

    Owner          = 0x10_000, // Implies Moderate, EditRoles, Invite (-> Join, Read), Write (-> Read)

    // Content-type capabilities (default-on; selectively stripped, e.g. for non-contact peer chats).
    Upload         = 0x10_0000, // Attach files/media to entries; implied by Write.
    WriteAudio     = 0x20_0000, // Record voice messages / start an audio stream; implied by Write.
    WriteVideo     = 0x40_0000, // Start a video stream / screenshare; implied by Write.
    ReadAudio      = 0x80_0000, // Receive audio streams; implied by Read.
    ReadVideo      = 0x100_0000, // Receive video streams; implied by Read.
}
