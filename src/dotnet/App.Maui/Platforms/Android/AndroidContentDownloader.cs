using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

public sealed class AndroidContentDownloader(IServiceProvider services)
{
    private const string Prefix = "/in/content/";

    private ILogger Log => field ??= services.LogFor(GetType());

    public static bool CanHandleWebRequestUri(string? relativeUrl)
        => relativeUrl.OrdinalStartsWith(Prefix);

    public static string CreateWebRequestUri(string url)
        => Prefix + System.Uri.EscapeDataString(url);

    public AttachFileInfo[] ConvertToAttachFileInfos(IEnumerable<Uri> uris)
    {
        var fileInfos = new List<AttachFileInfo>();
        foreach (var uri in uris) {
            var sUri = uri.ToString()!;
            var (stream, mimeType) = OpenInputStream(sUri);
            if (stream is null)
                continue;

            mimeType ??= "";
            if (!TryExtractFileName(sUri, out var fileName))
                fileName = "unknown";
            var fileLength = stream.Length;
            var fileProvider = new MauiFileProvider {
                FileRef = sUri,
                Metadata = new() {
                    FileName = fileName,
                    FileType = mimeType,
                    Length = fileLength,
                },
            };
            fileProvider.Initialize(services);
            fileInfos.Add(new AttachFileInfo(fileProvider));
        }
        return fileInfos.ToArray();
    }

    public bool TryExtractFileName(string url, out string fileName)
    {
        fileName = "";
        try {
            var uri = Uri.Parse(url);
            if (uri is null)
                return false;

            if (OrdinalEquals(uri.Scheme, "content")) {
                var contentResolver = Platform.AppContext.ContentResolver!;
                var cursor = contentResolver.Query(uri, null, null, null, null);
                if (cursor is not null) {
                    try {
                        if (cursor.MoveToFirst()) {
                            int nameIndex = cursor.GetColumnIndex(Android.Provider.IOpenableColumns.DisplayName);
                            if (nameIndex != -1) {
                                var s = cursor.GetString(nameIndex);
                                if (!s.IsNullOrEmpty()) {
                                    fileName = s;
                                    return true;
                                }
                            }
                        }
                    }
                    finally {
                        cursor.Close();
                    }
                }
            }

            if (uri.Path == null)
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

    public (Stream?, string?) GetWebRequestStream(string requestUri)
    {
        if (!requestUri.OrdinalStartsWith(Prefix))
            return (null, null);

        var uri = System.Uri.UnescapeDataString(requestUri[Prefix.Length..]);
        try {
            return OpenInputStream(uri);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to open stream for uri: '{Url}'", uri);
            return (null, null);
        }
    }

    public (Stream?, string?) OpenInputStream(string url)
    {
        try {
            var uri = Uri.Parse(url);
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
