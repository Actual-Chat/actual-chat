namespace ActualChat;

/// <summary>
/// Allowlist for post-authentication redirect targets, which arrive as
/// untrusted query parameters on the close-flow endpoint.
/// </summary>
public static class AuthRedirectUrl
{
    public static string? Sanitize(string? redirectUrl, IReadOnlySet<string> allowedHosts)
    {
        if (redirectUrl.IsNullOrEmpty())
            return null;
        // Browsers strip ASCII tab/LF/CR from anywhere in a URL before parsing it (WHATWG URL Standard),
        // so e.g. "/\t/evil.com" collapses to "//evil.com" client-side even though it looks host-relative here.
        if (redirectUrl.AsSpan().IndexOfAny('\t', '\n', '\r') >= 0)
            return null;
        if (!Uri.TryCreate(redirectUrl, UriKind.RelativeOrAbsolute, out var uri))
            return null;

        if (!uri.IsAbsoluteUri) {
            if (redirectUrl[0] != '/')
                return null;
            // "//host" and "/\host" are protocol-relative: browsers send them cross-origin.
            var isProtocolRelative = redirectUrl.Length > 1 && redirectUrl[1] is '/' or '\\';
            return isProtocolRelative ? null : redirectUrl;
        }

        if (IsAppScheme(uri))
            return string.Equals(uri.Host, Constants.AppSchemes.AuthCallbackHost, StringComparison.OrdinalIgnoreCase)
                ? redirectUrl
                : null;
        if (uri.Scheme is "http" or "https" && allowedHosts.Contains(uri.Host))
            return redirectUrl;
        return null;
    }

    public static bool IsAppScheme(string? url)
        => Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri) && IsAppScheme(uri);

    // Private methods

    private static bool IsAppScheme(Uri uri)
        => uri.IsAbsoluteUri && Constants.AppSchemes.All.Contains(uri.Scheme);
}
