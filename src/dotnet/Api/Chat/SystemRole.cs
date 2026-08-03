namespace ActualChat.Chat;

/// <summary>
/// Defines built-in system roles for chat participants.
/// </summary>
#pragma warning disable RCS1130

public enum SystemRole
{
    None = 0,
    Anyone = 11, // Any author who joined the chat
    Guest = Anyone + 2, // Unauthenticated user
    User = Anyone + 4, // Authenticated user in non-anonymous mode
    AnonymousUser = Anyone + 4 + 8, // Authenticated user in anonymous mode
    Moderator = 91,
    Owner = 101,
}
