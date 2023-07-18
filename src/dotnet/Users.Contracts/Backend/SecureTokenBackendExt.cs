namespace ActualChat.Users;

public static class SecureTokenBackendExt
{
    public static async ValueTask<string> Parse(this ISecureTokensBackend backend,
        string token, CancellationToken cancellationToken)
    {
        var parsed = await backend.TryParse(token, cancellationToken).ConfigureAwait(false);
        return parsed?.Value ?? throw StandardError.Unauthorized("Invalid secure token.");
    }
}
