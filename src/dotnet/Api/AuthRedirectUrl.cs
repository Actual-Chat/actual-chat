namespace ActualChat;

/// <summary>
/// Reduces post-authentication redirect targets, which arrive as untrusted query
/// parameters on the close-flow endpoint, to a URL local to this server.
/// </summary>
public static class AuthRedirectUrl
{
    public static string? Sanitize(string? redirectUrl)
    {
        if (redirectUrl.IsNullOrEmpty())
            return null;
        // Browsers strip ASCII tab/LF/CR from anywhere in a URL before parsing it (WHATWG URL Standard),
        // so e.g. "/\t/evil.com" collapses to "//evil.com" client-side even though it looks host-relative here.
        if (redirectUrl.AsSpan().IndexOfAny('\t', '\n', '\r') >= 0)
            return null;
        // UriKind.Absolute reads "/chat" as an implicit file path on Unix, so absoluteness is
        // decided via RelativeOrAbsolute, which answers the same way on every platform.
        if (!Uri.TryCreate(redirectUrl, UriKind.RelativeOrAbsolute, out var uri))
            return null;

        if (!uri.IsAbsoluteUri)
            return Reduce(redirectUrl);

        // An app scheme hands off to the installed app, so it's the one target that isn't ours to reduce
        if (IsAppScheme(uri))
            return string.Equals(uri.Host, Constants.AppSchemes.AuthCallbackHost, StringComparison.OrdinalIgnoreCase)
                ? redirectUrl
                : null;

        if (uri.Scheme is not ("http" or "https"))
            return null;

        // Whatever host was asked for, the redirect lands on this one
        return Reduce(uri.PathAndQuery + uri.Fragment);
    }

    public static bool IsAppScheme(string? url)
        => Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri) && IsAppScheme(uri);

    // Private methods

    private static bool IsAppScheme(Uri uri)
        => uri.IsAbsoluteUri && Constants.AppSchemes.All.Contains(uri.Scheme);

    private static string? Reduce(string url)
    {
        var localUrl = LocalUrl.Parse(url);
        return localUrl.IsTrulyLocal() ? localUrl.Value : null;
    }
}
