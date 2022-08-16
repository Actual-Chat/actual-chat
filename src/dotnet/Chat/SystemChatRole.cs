namespace ActualChat.Chat;

#pragma warning disable RCS1130

public enum SystemChatRole
{
    None = 0,
    Anyone = 11, // Any author
    Unauthenticated = Anyone + 2, // Unauthenticated authors in public chats
    Regular = Anyone + 4, // Regular author
    Anonymous = Anyone + 4 + 8, // Anonymous author
    Owner = 101,
}
