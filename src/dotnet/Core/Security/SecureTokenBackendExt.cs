namespace ActualChat.Security;

public static class SecureTokenBackendExt
{
    public static string Parse(
        this ISecureTokensBackend backend,
        string token,
        CancellationToken cancellationToken = default)
    {
        var secureValue = backend.TryParse(token);
        return secureValue?.Value ?? throw StandardError.Unauthorized("Invalid secure token.");
    }
}
