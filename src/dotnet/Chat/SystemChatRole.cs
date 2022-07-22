namespace ActualChat.Chat;

#pragma warning disable RCS1130

public enum SystemChatRole
{
    None = 0,
    Anyone = 1,
    Unauthenticated = Anyone + 2,
    Authenticated = Anyone + 4,
    Joined = Anyone + 10,
    JoinedUnauthenticated = Joined + 2,
    JoinedAuthenticated = Joined + 4,
    Owners = Anyone + 100,
}
