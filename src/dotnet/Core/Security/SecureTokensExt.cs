namespace ActualChat.Security;

public static class SecureTokensExt
{
    public static Task<SecureToken> CreateSessionToken(
        this ISecureTokens secureTokens,
        Session session,
        CancellationToken cancellationToken = default)
        => secureTokens.CreateForSession(session, cancellationToken);
}
