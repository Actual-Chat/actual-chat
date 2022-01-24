namespace ActualChat.Chat;

[Flags]
public enum ChatPermissions : uint
{
    None   = 0b_00000000_00000000_00000000_00000000,
    Read   = 0b_00000000_00000000_00000000_00000001,
    Write  = 0b_00000000_00000000_00000000_00000010,

    Invite = 0b_01000000_00000000_00000000_00000000,
    Admin  = 0b_10000000_00000000_00000000_00000000,
    Owner  = Admin | Invite,
    All = Owner | Read | Write,
}
