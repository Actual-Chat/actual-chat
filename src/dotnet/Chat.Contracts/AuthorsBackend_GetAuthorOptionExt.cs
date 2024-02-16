namespace ActualChat.Chat;

public static class AuthorsBackend_GetAuthorOptionExt
{
    public static bool IsRaw(this AuthorsBackend_GetAuthorOption option)
        => option is AuthorsBackend_GetAuthorOption.Raw;

    public static bool IsFull(this AuthorsBackend_GetAuthorOption option)
        => option is AuthorsBackend_GetAuthorOption.Full;
}
