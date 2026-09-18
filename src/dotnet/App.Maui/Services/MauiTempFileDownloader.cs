using ActualLab.Generators;
using ActualLab.IO;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Downloads remote content to the app cache folder - the staging area for handing
/// a file to the OS: the share sheet, "Save to Files", the photo library.
/// </summary>
public sealed class MauiTempFileDownloader(IServiceProvider services)
{
    private HttpClient HttpClient
        => field ??= services.HttpClientFactory().CreateClient(nameof(MauiTempFileDownloader));

    public async Task<FilePath> Download(
        string url,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);

        // A per-download subfolder keeps the real file name - which the share sheet shows and
        // "Save to Files" reuses - without colliding with earlier downloads.
        var downloadsFolder = Directory.CreateDirectory(Path.Combine(
            FileSystem.Current.CacheDirectory,
            "downloads",
            RandomStringGenerator.Default.Next()));
        var filePath = (FilePath)downloadsFolder.FullName & GetFileName(fileName, contentType);
        var fileStream = File.OpenWrite(filePath);
        await using var __ = fileStream.ConfigureAwait(false);
        await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        return filePath;
    }

    // Private methods

    private static string GetFileName(string fileName, string contentType)
    {
        if (!fileName.IsNullOrEmpty())
            return fileName;

        var extension = MediaTypeExt.GetFileExtension(contentType)
            ?? throw StandardError.Constraint("Not supported media type.");
        return "download" + extension;
    }
}
