using System.Text.RegularExpressions;
using ActualChat.Hosting;
using Cysharp.Text;

namespace ActualChat;

public sealed class UrlMapper
{
    private static readonly Regex IsAbsoluteUrlRe = new(@"^[\w\d]+://", RegexOptions.Compiled);

    private string _baseUrlWithoutBackslash;

    public Uri BaseUri { get; }
    public bool IsActualChat { get; }
    public bool IsDevActualChat { get; }
    public bool IsLocalActualChat { get; }
    public bool HasImageProxy { get; }

    public string BaseUrl { get; }
    public string ApiBaseUrl { get; }
    public string ContentBaseUrl { get; }
    public string ImageProxyBaseUrl { get; }

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
    }

    public static bool IsAbsolute(string url)
        => IsAbsoluteUrlRe.IsMatch(url);

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

    // Returns absolute URL
    public string ContentUrl(string contentId)
        => ToAbsolute(ContentBaseUrl, contentId, true);

    // Returns absolute URL
    public string ImagePreviewUrl(string imageUrl, int maxWidth, int maxHeight)
    {
        if (!HasImageProxy)
            return imageUrl;

        var sMaxWidth = maxWidth.ToString(CultureInfo.InvariantCulture);
        var sMaxHeight = maxHeight.ToString(CultureInfo.InvariantCulture);
        return $"{ImageProxyBaseUrl}{sMaxWidth}x{sMaxHeight},fit/{imageUrl}";
    }

    // Returns absolute URL
    public string ImagePreview128Url(string imageUrl)
        => HasImageProxy ? $"{ImageProxyBaseUrl}128/{imageUrl}" : imageUrl;
}
