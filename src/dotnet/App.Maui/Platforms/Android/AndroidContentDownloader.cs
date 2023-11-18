using ActualChat.Chat.UI.Blazor.Services;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

public sealed class AndroidContentDownloader(IServiceProvider services) : IIncomingShareFileDownloader
{
    private const string Prefix = "/in/content/";
    private const string ContentSchemePrefix = "content://";

    private ILogger? _log;
    private ILogger Log => _log ??= services.LogFor(GetType());

#pragma warning disable CA1822 // Can be static
    public bool CanHandlePath(string? relativeUrl)
#pragma warning restore CA1822
        => relativeUrl.OrdinalStartsWith(Prefix);

    public static bool TryCreateAppHostRelativeUrl(string url, out string relativeUrl)
    {
        relativeUrl = "";
        if (!url.OrdinalStartsWith(ContentSchemePrefix))
            return false;
        relativeUrl = Prefix + url[ContentSchemePrefix.Length..];
        return true;
    }

    public bool TryExtractFileName(string url, out string fileName)
    {
        fileName = "";
        try {
            var uri = Uri.Parse(url);
            if (uri?.Path == null)
                return false;
            var index = uri.Path.LastIndexOf('/');
            if (index == -1)
                return false;
            fileName = uri.Path.Substring(index + 1);
            return true;
        }
        catch {
            return false;
        }
    }

    public (Stream?, string?) OpenInputStream(string url)
    {
        if (!url.OrdinalStartsWith(Prefix))
            throw new ArgumentOutOfRangeException(nameof(url), "Invalid url");

        var url2 = ContentSchemePrefix + url[Prefix.Length..];
        try {
            var uri = Uri.Parse(url2);
            if (uri == null) {
                Log.LogWarning("Can not perform request for uri: '{Url}'. Failed to parse Uri", url);
                return (null, null);
            }
            var contentResolver = Platform.AppContext.ContentResolver!;
            var stream = contentResolver.OpenInputStream(uri);
            if (stream == null)
                return (null, null);
            var mimeType = contentResolver.GetType(uri);
            return (stream, mimeType);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to perform request for uri: '{Url}'", url);
            return (null, null);
        }
    }
}
