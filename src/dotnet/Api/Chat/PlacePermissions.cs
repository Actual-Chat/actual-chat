namespace ActualChat.Chat;

/// <summary>
/// Defines permission flags for place operations.
/// </summary>
#pragma warning disable MA0062
#pragma warning disable RCS1157

[Flags]
public enum PlacePermissions
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
}
