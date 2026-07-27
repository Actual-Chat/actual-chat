namespace ActualChat.Security;

public static class SecureTokenBackendExt
{
    public static string Decrypt(this ISecureTokensBackend backend, SecureTokenKind kind, string token)
    {
        var decrypted = backend.TryDecrypt(kind, token);
        return decrypted?.Value ?? throw StandardError.Unauthorized("Invalid secure token.");
    }

    public static Session? TryDecryptSessionToken(this ISecureTokensBackend backend, string sessionToken)
    {
        var decrypted = backend.TryDecrypt(SecureTokenKind.Session, sessionToken);
        return SessionExt.NewValidOrNull(decrypted?.Value);
    }

    public static Session DecryptSessionToken(this ISecureTokensBackend backend, string sessionToken)
    {
        var decrypted = backend.Decrypt(SecureTokenKind.Session, sessionToken);
        return new Session(decrypted).RequireValid();
    }
}
