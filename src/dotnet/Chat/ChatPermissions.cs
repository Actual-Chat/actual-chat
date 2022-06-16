namespace ActualChat.Chat;

#pragma warning disable MA0062

[Flags]
public enum ChatPermissions
{
    Read           = 0x1,
    Write          = 0x2,
    ReadWrite      = Read + Write,

    Invite         = 0x100 + ReadWrite,

    EditProperties = 0x1000 + Invite,
    Owner          = 0x10_000 + EditProperties,
}
