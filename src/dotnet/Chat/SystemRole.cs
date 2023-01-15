namespace ActualChat.Chat;

#pragma warning disable RCS1130

public enum SystemRole
{
    None = 0,
    Anyone = 11, // Any author who joined the chat
    Guest = Anyone + 2, // Unauthenticated user
    User = Anyone + 4, // Authenticated user in non-anonymous mode
    AnonymousUser = Anyone + 4 + 8, // Authenticated user in anonymous mode
    Owner = 101,
}
