using ActualChat.Chat.UI.Blazor.Services;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

public class AndroidContentDownloader : IIncomingShareFileDownloader
{
    private const string Prefix = "/in/content/";
    private const string ContentSchemePrefix = "content://";

    private ILogger<AndroidContentDownloader> Log { get; }

    public AndroidContentDownloader(ILogger<AndroidContentDownloader> log)
        => Log = log;

    public bool CanHandlePath(string relativeUrl)
        => relativeUrl.OrdinalStartsWith(Prefix);

    public static bool TryCreateAppHostRelativeUrl(string url, out string relativeUrl)
    {
        relativeUrl = "";
        if (!url.OrdinalStartsWith(ContentSchemePrefix))
            return false;
        relativeUrl = Prefix + url.Substring(ContentSchemePrefix.Length);
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

        var url2 = ContentSchemePrefix + url.Substring(Prefix.Length);
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
