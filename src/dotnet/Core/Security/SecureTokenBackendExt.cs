namespace ActualChat.Security;

public static class SecureTokenBackendExt
{
    public static string Parse(this ISecureTokensBackend backend, string token)
    {
        var secureValue = backend.TryParse(token);
        return secureValue?.Value ?? throw StandardError.Unauthorized("Invalid secure token.");
    }

    public static Session ParseSessionToken(this ISecureTokensBackend backend, string sessionToken)
    {
        var sessionId = backend.Parse(sessionToken);
        return new Session(sessionId).RequireValid();
    }
}
