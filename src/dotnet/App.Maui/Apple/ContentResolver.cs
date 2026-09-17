using System.Diagnostics.CodeAnalysis;

namespace ActualChat.App.Maui;

public static class ContentResolver
{
    public const string UriContentScheme = UrlMapper.UriContentScheme;
    public const string FilesContentProvider = "files";
    public const string MediaContentProvider = "media";
    private const string FilesContentPrefix = $"{UriContentScheme}://{FilesContentProvider}/";
    private const string MediaContentPrefix = $"{UriContentScheme}://{MediaContentProvider}/";

    public static string GetFileUri(string filePath)
        => $"{FilesContentPrefix}{LocalContentRegistry.GetOrAddKey(filePath)}";

    // WKWebView refuses a scheme handler for https, so remote media is rendered through this
    // scheme instead and ContentSchemeHandler resolves the key back to the canonical URL
    public static string GetMediaUri(string url)
        => url.StartsWith(UriContentScheme + "://", StringComparison.OrdinalIgnoreCase)
            ? url
            : $"{MediaContentPrefix}{LocalContentRegistry.GetOrAddKey(url)}";

    public static void InstallUrlConverters()
    {
        UrlMapper.ToCacheUrlConverter = GetMediaUri;
        UrlMapper.ToOriginConverter = static url
            => TryGetMediaUrlFromUri(url, out var originUrl) ? originUrl : url;
    }

    public static bool TryGetMediaUrlFromUri(string uri, [NotNullWhen(true)] out string? url)
    {
        url = null;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
            return false;
        if (parsedUri.Host != MediaContentProvider || !parsedUri.IsDefaultPort)
            return false;

        return LocalContentRegistry.TryGetContentRef(parsedUri.AbsolutePath.TrimStart('/'), out url);
    }

    public static bool TryGetFilePathFromUri(string uri, [NotNullWhen(true)] out string? filePath)
    {
        filePath = null;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var uri1))
            return false;

        if (uri1.Host != FilesContentProvider || !uri1.IsDefaultPort)
            return false;

        return LocalContentRegistry.TryGetContentRef(uri1.AbsolutePath.TrimStart('/'), out filePath);
    }
}
