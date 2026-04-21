namespace ActualChat.Security;

public static class SecureTokenBackendExt
{
    public static string Decrypt(this ISecureTokensBackend backend, string token)
    {
        var decrypted = backend.TryDecrypt(token);
        return decrypted?.Value ?? throw StandardError.Unauthorized("Invalid secure token.");
    }

    public static Session? TryDecryptSessionToken(this ISecureTokensBackend backend, string sessionToken)
    {
        var decrypted = backend.TryDecrypt(sessionToken);
        return SessionExt.NewValidOrNull(decrypted?.Value);
    }

    public static Session DecryptSessionToken(this ISecureTokensBackend backend, string sessionToken)
    {
        var decrypted = backend.Decrypt(sessionToken);
        return new Session(decrypted).RequireValid();
    }
}
