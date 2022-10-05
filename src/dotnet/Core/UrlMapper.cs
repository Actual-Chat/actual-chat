using System.Text.RegularExpressions;
using ActualChat.Hosting;

namespace ActualChat;

public sealed class UrlMapper
{
    private static readonly Regex IsAbsoluteUrlRe = new(@"^[\w\d]+://", RegexOptions.Compiled);

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

    public string ToAbsolute(string relativeUrl, bool allowAbsoluteUrl = false)
        => ToAbsolute(BaseUrl, relativeUrl, allowAbsoluteUrl);

    public string ToAbsolute(string baseUrl, string relativeUrl, bool allowAbsoluteUrl = false)
    {
        if (IsAbsolute(relativeUrl))
            return allowAbsoluteUrl ? relativeUrl : throw new ArgumentOutOfRangeException(relativeUrl);
        return baseUrl + relativeUrl.TrimStart('/');
    }

    public string ContentUrl(string contentId)
        => ToAbsolute(ContentBaseUrl, contentId, true);

    public string ImagePreviewUrl(string imageUrl, int maxWidth, int maxHeight)
    {
        if (!HasImageProxy)
            return imageUrl;

        var sMaxWidth = maxWidth.ToString(CultureInfo.InvariantCulture);
        var sMaxHeight = maxHeight.ToString(CultureInfo.InvariantCulture);
        return $"{ImageProxyBaseUrl}{sMaxWidth}x{sMaxHeight},fit/{imageUrl}";
    }

    public string ImagePreview128Url(string imageUrl)
        => HasImageProxy ? $"{ImageProxyBaseUrl}128/{imageUrl}" : imageUrl;
}
