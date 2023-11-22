using System.Numerics;
using System.Text.RegularExpressions;
using ActualChat.Hosting;

namespace ActualChat;

public sealed partial class UrlMapper
{
    [GeneratedRegex(@"^[\w\d]+://")]
    private static partial Regex IsAbsoluteUrlRegexFactory();

    private static readonly Regex IsAbsoluteUrlRegex = IsAbsoluteUrlRegexFactory();
    private static readonly char[] UriPathEndChar = { '#', '?' };
    private static readonly string[] ExtensionsToExclude = { ".svg", ".gif" };

    private readonly string _baseUrlWithoutBackslash;

    public Uri BaseUri { get; }
    public bool IsActualChat { get; }
    public bool IsDevActualChat { get; }
    public bool IsLocalActualChat { get; }
    public bool HasImageProxy { get; }

    public string BaseUrl { get; }
    public string ApiBaseUrl { get; }
    public string ContentBaseUrl { get; }
    public string ImageProxyBaseUrl { get; }
    public string BoringAvatarsProxyBaseUrl { get; }
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
        IsActualChat = OrdinalIgnoreCaseEquals(BaseUri.Host, "actual.chat");
        IsDevActualChat = OrdinalIgnoreCaseEquals(BaseUri.Host, "dev.actual.chat");
        IsLocalActualChat = OrdinalIgnoreCaseEquals(BaseUri.Host, "local.actual.chat");

        ApiBaseUrl = $"{BaseUrl}api/";
        ContentBaseUrl = $"{ApiBaseUrl}content/";
        BoringAvatarsProxyBaseUrl = $"{BaseUrl}boringavatars/";
        ImageProxyBaseUrl = "";
        HasImageProxy = false;
        if (IsActualChat || IsLocalActualChat) {
            ContentBaseUrl = $"{BaseUri.Scheme}://cdn.{BaseUri.Host}/";
            ImageProxyBaseUrl = $"{BaseUri.Scheme}://media.{BaseUri.Host}/";
            HasImageProxy = true;
        }
        else if (IsDevActualChat) {
            ContentBaseUrl = $"{BaseUri.Scheme}://cdn-{BaseUri.Host}/";
            ImageProxyBaseUrl = $"{BaseUri.Scheme}://media-{BaseUri.Host}/";
            HasImageProxy = true;
        }
        WebsocketBaseUrl = GetWebSocketUrl(_baseUrlWithoutBackslash);
    }

    public static bool IsAbsolute(string url)
        => IsAbsoluteUrlRegex.IsMatch(url);

    public static string GetWebSocketUrl(string url)
    {
        if (url.OrdinalStartsWith("ws://")
            || url.OrdinalStartsWith("wss://"))
            return url;

        if (url.OrdinalStartsWith("http://"))
            return "ws://" + url[7..];
        if (url.OrdinalStartsWith("https://"))
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
    /// <param name="uri">An absolute URI that is within the space of the base URI.</param>
    /// <returns>A relative URI path.</returns>
    public string ToBaseRelativePath(string uri)
    {
        if (uri.OrdinalStartsWith(BaseUri!.OriginalString))
        {
            // The absolute URI must be of the form "{baseUri}something" (where
            // baseUri ends with a slash), and from that we return "something"
            return uri.Substring(BaseUri.OriginalString.Length);
        }

        var pathEndIndex = uri.IndexOfAny(UriPathEndChar);
        var uriPathOnly = pathEndIndex < 0 ? uri : uri.Substring(0, pathEndIndex);
        if (OrdinalEquals($"{uriPathOnly}/", BaseUri.OriginalString))
        {
            // Special case: for the base URI "/something/", if you're at
            // "/something" then treat it as if you were at "/something/" (i.e.,
            // with the trailing slash). It's a bit ambiguous because we don't know
            // whether the server would return the same page whether or not the
            // slash is present, but ASP.NET Core at least does by default when
            // using PathBase.
            return uri.Substring(BaseUri.OriginalString.Length - 1);
        }

        var message = $"The URI '{uri}' is not contained by the base URI '{BaseUri}'.";
        throw new ArgumentException(message);
    }

    // Returns absolute URL
    public string ContentUrl(string contentId)
        => ToAbsolute(ContentBaseUrl, contentId, true);

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

        if (imageUrl.StartsWith("https://source.boringavatars.com/", StringComparison.OrdinalIgnoreCase))
            return imageUrl;

        var sMaxWidth = maxWidth?.Format();
        var sMaxHeight = maxHeight?.Format();
        return $"{ImageProxyBaseUrl}{sMaxWidth}x{sMaxHeight}/{imageUrl}";
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

        return $"{ImageProxyBaseUrl}128/{imageUrl}";
    }

    // Returns absolute URL
    public string BoringAvatar(string imageUrl)
        => imageUrl.OrdinalReplace(DefaultUserPicture.BoringAvatarsBaseUrl, BoringAvatarsProxyBaseUrl);
}
