using ActualChat.IO;
using ActualLab.Generators;
using ActualLab.IO;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Downloads remote content to the app cache folder - the staging area for handing
/// a file to the OS: the share sheet, "Save to Files", the photo library.
/// </summary>
public sealed class MauiTempFileDownloader(IServiceProvider services)
{
    private static readonly FilePath RootDirectory = new FilePath(FileSystem.CacheDirectory) | "downloads";

    // Long enough for the app another one handed the file to to finish reading it
    private static readonly TimeSpan StaleAge = TimeSpan.FromMinutes(30);

    private HttpClient HttpClient
        => field ??= services.HttpClientFactory().CreateClient(nameof(MauiTempFileDownloader));
    private ILogger Log => field ??= services.LogFor(GetType());

    public async Task<FilePath> Download(
        string url,
        string fileName,
        string contentType,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RemoveStale();

        using var response = await Fetch(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength;
        var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = source.ConfigureAwait(false);

        // A per-download subfolder keeps the real file name - which the share sheet shows and
        // "Save to Files" reuses - without colliding with earlier downloads.
        var folder = Directory.CreateDirectory(RootDirectory & RandomStringGenerator.Default.Next());
        var filePath = (FilePath)folder.FullName & GetFileName(fileName, contentType);
        await source.CopyToFile(filePath, length, progress, cancellationToken).ConfigureAwait(false);
        return filePath;
    }

    // Private methods

    private async Task<HttpResponseMessage> Fetch(string url, CancellationToken cancellationToken)
    {
        // The bytes are often on disk already - the WebView serves our own content from an
        // encrypted cache, and a miss there downloads once and fills it for the next reader.
        var cached = await MauiContentRequests.HandleAsync(url, "GET", null).ConfigureAwait(false);
        return cached
            ?? await HttpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
    }


    // Every file handed to the OS stays readable by it, i.e. unencrypted, unlike the content
    // cache - so the copies are swept rather than kept around until the OS clears the folder.
    private void RemoveStale()
    {
        try {
            if (!RootDirectory.DirectoryExists)
                return;

            var minWriteTime = DateTime.UtcNow - StaleAge;
            foreach (var folder in Directory.EnumerateDirectories(RootDirectory)) {
                if (Directory.GetLastWriteTimeUtc(folder) > minWriteTime)
                    continue;

                Directory.Delete(folder, true);
            }
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to remove stale downloads");
        }
    }

    private static string GetFileName(string fileName, string contentType)
    {
        if (!fileName.IsNullOrEmpty())
            return fileName;

        var extension = MediaTypeExt.GetFileExtension(contentType)
            ?? throw StandardError.Constraint("Not supported media type.");
        return "download" + extension;
    }
}
