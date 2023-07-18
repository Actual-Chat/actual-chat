namespace ActualChat.Security;

public static class SecureTokensExt
{
    public static Task<SecureToken> CreateForDefaultSession(
        this ISecureTokens secureTokens,
        CancellationToken cancellationToken = default)
        => secureTokens.CreateForSession(Session.Default, cancellationToken);
}
