namespace ActualChat.Chat;

#pragma warning disable MA0062
#pragma warning disable RCS1157

[Flags]
public enum ChatPermissions
{
    Read           = 0x1,
    Write          = 0x2,
    SeeMembers     = 0x4,

    Join           = 0x80,
    Invite         = 0x100,
    Leave          = 0x200,

    EditProperties = 0x1000,
    EditRoles      = 0x2000,

    Owner          = 0x10_000,
}
