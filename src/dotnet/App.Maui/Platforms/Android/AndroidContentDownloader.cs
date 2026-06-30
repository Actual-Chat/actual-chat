using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

public sealed class AndroidContentDownloader(IServiceProvider services)
{
    private const string Prefix = "/in/content/";
    private static readonly FilePath IncomingShareCacheDir = new FilePath(FileSystem.CacheDirectory) | "incoming-share";
    private ILogger Log => field ??= services.LogFor(GetType());

    public static bool CanHandleWebRequestUri(string? relativeUrl)
        => (relativeUrl ?? "").StartsWith(Prefix);

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

            // Copy now while the transient share grant is alive — the source URI dies with this process,
            // so later uploads and post-restart retries must read our local copy instead.
            var fileRef = CopyToCache(stream, fileName, out var fileLength);
            if (fileRef.IsNullOrEmpty())
                continue;

            var fileProvider = new MauiFileProvider {
                FileRef = fileRef,
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

    public static void DeleteCachedShareFile(string uri)
    {
        if (!uri.StartsWith(System.Uri.UriSchemeFile))
            return;
        try {
            var path = (FilePath)new System.Uri(uri).LocalPath;
            if (path.IsSubPathOf(IncomingShareCacheDir) && File.Exists(path))
                File.Delete(path);
        }
        catch {
            // Best-effort cleanup; the OS evicts the cache directory under storage pressure anyway.
        }
    }

    private string CopyToCache(Stream source, FilePath fileName, out long length)
    {
        length = 0;
        try {
            Directory.CreateDirectory(IncomingShareCacheDir);
            var localName = fileName.ToUnique();
            var localPath = IncomingShareCacheDir | localName;
            using (source)
            using (var target = File.Create(localPath)) {
                source.CopyTo(target);
                length = target.Length;
            }
            return Uri.FromFile(new Java.IO.File(localPath.Value))!.ToString()!;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to copy shared file '{FileName}' to app-private storage", fileName);
            return "";
        }
    }

    public bool TryExtractFileName(string url, out FilePath fileName)
    {
        fileName = "";
        try {
            var uri = Uri.Parse(url);
            if (uri is null)
                return false;

            if (uri.Scheme == "content") {
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
        if (!requestUri.StartsWith(Prefix))
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
