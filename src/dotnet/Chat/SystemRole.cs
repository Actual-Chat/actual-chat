namespace ActualChat.Chat;

#pragma warning disable RCS1130

public enum SystemRole
{
    None = 0,
    Anyone = 11, // Any author
    Guest = Anyone + 2, // Unauthenticated accounts in public chats
    Regular = Anyone + 4, // Regular author
    Anonymous = Anyone + 4 + 8, // Anonymous author
    Owner = 101,
}
