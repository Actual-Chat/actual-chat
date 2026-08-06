using System.Numerics;
using System.Text.RegularExpressions;

namespace ActualChat;

/// <summary>
/// Maps local URLs to absolute URLs and provides content/image proxy URLs.
/// </summary>
public sealed partial class UrlMapper
{
    /// <summary>
    /// URI scheme used by MAUI apps (iOS/macOS/Windows) for local file content.
    /// </summary>
    public const string UriContentScheme = "content";

    [GeneratedRegex(@"^[\w\d]+://")]
    private static partial Regex IsAbsoluteUrlRegexFactory();

    private static readonly Regex IsAbsoluteUrlRegex = IsAbsoluteUrlRegexFactory();
    private static readonly char[] UriPathEndChar = ['#', '?'];
    private static readonly string[] ExtensionsToExclude = [".gif"];

    // Trusted hosts whose GIFs we auto-render as <img>. Anything else falls back to
    // a plain link to avoid turning arbitrary URLs into tracking pixels.
    private static readonly HashSet<string> TrustedGifHosts = new(StringComparer.OrdinalIgnoreCase) {
        "static.klipy.com",
    };

    private readonly string _baseUrlWithoutBackslash;

    public Uri BaseUri { get; }
    public bool IsVoxt { get; }
    public bool IsDevVoxt { get; }
    public bool IsLocalVoxt { get; }
    public bool HasImageProxy { get; }

    public string BaseUrl { get; }
    public string ApiBaseUrl { get; }
    public string ContentBaseUrl { get; }
    public string ImageProxyBaseUrl { get; }
    public string MapTilesBaseUrl { get; }
    public string WebsocketBaseUrl { get; }

    public UrlMapper(HostInfo hostInfo) : this(hostInfo.BaseUrl) { }
    public UrlMapper(string baseUrl)
    {
        if (!IsAbsolute(baseUrl))
            throw StandardError.Internal("BaseUrl must be absolute.");

        // Normalize baseUri
        baseUrl = baseUrl.EnsureSuffix("/");
        _baseUrlWithoutBackslash = baseUrl.TrimSuffix("/");
        BaseUrl = baseUrl;
        BaseUri = baseUrl.ToUri();
        IsVoxt = string.Equals(BaseUri.Host, Constants.Hosts.Voxt, StringComparison.OrdinalIgnoreCase);
        IsDevVoxt = string.Equals(BaseUri.Host, Constants.Hosts.DevVoxt, StringComparison.OrdinalIgnoreCase);
        // Support worktree subdomains: wt1.local.voxt.ai, etc.
        IsLocalVoxt = Constants.Hosts.IsLocalDev(BaseUri.Host);

        ApiBaseUrl = $"{BaseUrl}api/";
        ContentBaseUrl = $"{ApiBaseUrl}content/";
        ImageProxyBaseUrl = "";
        MapTilesBaseUrl = "";
        HasImageProxy = false;
        if (IsVoxt || IsDevVoxt || IsLocalVoxt) {
            var cdnSubdomainSeparator =
                BaseUri.Host.EndsWith(Constants.Hosts.LocalVoxtSuffix, StringComparison.OrdinalIgnoreCase)
                    ? '-'
                    : '.';
            ContentBaseUrl = $"{BaseUri.Scheme}://cdn{cdnSubdomainSeparator}{BaseUri.Host}/";
            ImageProxyBaseUrl = $"{BaseUri.Scheme}://media{cdnSubdomainSeparator}{BaseUri.Host}/";
            MapTilesBaseUrl = $"{BaseUri.Scheme}://maps{cdnSubdomainSeparator}{BaseUri.Host}/";
            HasImageProxy = true;
        }
        WebsocketBaseUrl = GetWebSocketUrl(_baseUrlWithoutBackslash);
    }

    public static bool IsAbsolute(string url)
        => IsAbsoluteUrlRegex.IsMatch(url);

    // True if `url` is a https URL on an allowlisted GIF host (klipy, ...).
    public static bool IsTrustedGifHostUrl(string url)
        => !url.IsNullOrEmpty()
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && TrustedGifHosts.Contains(uri.Host);

    // True if `url` is a https GIF on an allowlisted host (rendered as <img> via image proxy).
    public static bool IsTrustedGifUrl(string url)
        => !url.IsNullOrEmpty()
            && url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
            && IsTrustedGifHostUrl(url);

    // True if `url` points to our own uploaded content (cdn.../...) or image-proxy
    // media (media.../...) — these are surfaced via the Media/Files tabs.
    public bool IsOwnContentUrl(string url)
        => !url.IsNullOrEmpty()
            && (url.StartsWith(ContentBaseUrl, StringComparison.OrdinalIgnoreCase)
                || (HasImageProxy && url.StartsWith(ImageProxyBaseUrl, StringComparison.OrdinalIgnoreCase)));

    // True for URLs that must stay out of the Links index: trusted-GIF hosts (rendered
    // inline as <img>) and our own content/media (shown in the Media/Files tabs).
    public bool IsExcludedFromLinkIndex(string url)
        => IsTrustedGifHostUrl(url) || IsOwnContentUrl(url);

    public static string GetWebSocketUrl(string url)
    {
        if (url.StartsWith("ws://")
            || url.StartsWith("wss://"))
            return url;

        if (url.StartsWith("http://"))
            return "ws://" + url[7..];
        if (url.StartsWith("https://"))
            return "wss://" + url[8..];

        // No prefix at all
        return "wss://" + url;
    }

    public string ToAbsolute(string url, bool allowAbsoluteUrl = false)
        => ToAbsolute(BaseUrl, url, allowAbsoluteUrl);

    public string ToAbsolute(string baseUrl, string url, bool allowAbsoluteUrl = false)
    {
        if (IsAbsolute(url))
            return allowAbsoluteUrl ? url : throw new ArgumentOutOfRangeException(url);
        if (ReferenceEquals(baseUrl, BaseUrl)) // A bit more efficient shortcut for BaseUrl
            return url.Length != 0 && url[0] == '/'
                ? _baseUrlWithoutBackslash + url
                : baseUrl + url;
        return baseUrl + url.TrimStart('/');
    }

    /// <summary>
    /// Given a base URI (e.g., one previously returned by <see cref="BaseUri"/>),
    /// converts an absolute URI into one relative to the base URI prefix.
    /// </summary>
    /// <param name="url">An absolute URI that is within the space of the base URI.</param>
    /// <returns>A relative URI path.</returns>
    public string ToBaseRelativePath(string url)
    {
        if (url.StartsWith(BaseUri.OriginalString))
        {
            // The absolute URI must be of the form "{baseUri}something" (where
            // baseUri ends with a slash), and from that we return "something"
            return url.Substring(BaseUri.OriginalString.Length);
        }

        var pathEndIndex = url.IndexOfAny(UriPathEndChar);
        var uriPathOnly = pathEndIndex < 0 ? url : url.Substring(0, pathEndIndex);
        if ($"{uriPathOnly}/" == BaseUri.OriginalString)
        {
            // Special case: for the base URI "/something/", if you're at
            // "/something" then treat it as if you were at "/something/" (i.e.,
            // with the trailing slash). It's a bit ambiguous because we don't know
            // whether the server would return the same page whether or not the
            // slash is present, but ASP.NET Core at least does by default when
            // using PathBase.
            return url.Substring(BaseUri.OriginalString.Length - 1);
        }

        var message = $"The URI '{url}' is not contained by the base URI '{BaseUri}'.";
        throw new ArgumentException(message);
    }

    // Returns absolute URL
    public string ContentUrl(string contentId)
        => ToAbsolute(ContentBaseUrl, contentId, true);

    // Returns absolute URL that forces a download: content URLs are always cross-origin,
    // where <a download> is ignored, so only Content-Disposition can trigger one.
    public string ContentDownloadUrl(string contentId)
        => ContentUrl(contentId) + "?download=1";

    // Returns absolute URL
    public string ImagePreviewUrl(string imageUrl, Vector2 maxResolution)
        => ImagePreviewUrl(imageUrl, (int)maxResolution.X, (int)maxResolution.Y);

    // Returns absolute URL
    public string ImagePreviewUrl(string imageUrl, int? maxWidth, int? maxHeight)
    {
        if (!HasImageProxy)
            return imageUrl;

        if (imageUrl.IsNullOrEmpty())
            return "";

        var extension = Path.GetExtension(imageUrl);
        if (ExtensionsToExclude.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return imageUrl;

        var sMaxWidth = maxWidth?.Format();
        var sMaxHeight = maxHeight?.Format();
        return $"{ImageProxyBaseUrl}{sMaxWidth}x{sMaxHeight}/{imageUrl}";
    }

    // Returns absolute URL routed through the image proxy in passthrough mode (no resize).
    // Used for GIFs where animation must be preserved — willnorris/imageproxy treats "0"
    // as "no transformation", so the original bytes are streamed as-is.
    // Returns "" if image proxy is not available — caller should fall back to a plain link.
    public string GifProxyUrl(string gifUrl)
    {
        if (!HasImageProxy || gifUrl.IsNullOrEmpty())
            return "";

        return $"{ImageProxyBaseUrl}0/{gifUrl}";
    }

    // Returns absolute URL
    public string ImagePreview128Url(string imageUrl)
    {
        if (!HasImageProxy)
            return imageUrl;

        if (imageUrl.IsNullOrEmpty())
            return "";

        var imageExtension = Path.GetExtension(imageUrl);
        if (ExtensionsToExclude.Contains(imageExtension, StringComparer.OrdinalIgnoreCase))
            return imageUrl;

        if (imageUrl.StartsWith("https://api.dicebear.com", StringComparison.OrdinalIgnoreCase))
            return imageUrl;

        // TODO(AK): Add CSP for gravatar and update all mobile apps and uncomment this after some period
        // if (imageUrl.StartsWith("https://www.gravatar.com", StringComparison.OrdinalIgnoreCase))
        //     return imageUrl;

        return $"{ImageProxyBaseUrl}128/{imageUrl}";
    }
}
