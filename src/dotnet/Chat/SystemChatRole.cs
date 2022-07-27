namespace ActualChat.Chat;

#pragma warning disable RCS1130

public enum SystemChatRole
{
    None = 0,
    Anyone = 11,
    Unauthenticated = Anyone + 2,
    Regular = Anyone + 4,
    Anonymous = Anyone + 4 + 8,
    Owner = 101,
}
