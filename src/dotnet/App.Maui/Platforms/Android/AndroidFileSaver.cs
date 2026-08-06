using ActualChat.UI.Blazor.Services;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using Android.Provider;
using File = Java.IO.File;
using Environment = Android.OS.Environment;
using Stream = System.IO.Stream;
using JObject = Java.Lang.Object;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

public class AndroidFileSaver(IServiceProvider services)
    : IFileSaver
{
    private const string AppSubFolder = CoreConstants.AppName;

    private ToastUI ToastUI => field ??= services.GetRequiredService<ToastUI>();
    private IHttpClientFactory HttpClientFactory => field ??= services.GetRequiredService<IHttpClientFactory>();
    private ILogger Log => field ??= services.LogFor(GetType());

    public async Task Save(string url, string fileName, string contentType)
    {
        // TODO(DF): Add special handling to ensure reliable file loading. Provide visual feedback for long loading files.
        var succeeded = await BackgroundTask.Run(
                () => SaveInternally(url, contentType, fileName),
            CancellationToken.None)
            .ConfigureAwait(true); // Continue on Blazor context.

        var target = GetContentKind(contentType) switch {
            ContentKind.Image or ContentKind.Video => "the gallery",
            ContentKind.Audio => "Music",
            _ => "Downloads",
        };
        if (succeeded)
            ToastUI.Show($"1 file saved to {target}", "icon-checkmark-circle-2", ToastDismissDelay.Short);
        else
            ToastUI.Show($"Failed to save to {target}", "icon-alert-circle", ToastDismissDelay.Long);
    }

    private async Task<bool> SaveInternally(string sUri, string contentType, string fileName)
    {
        var succeeded = false;
        try {
            using var client = HttpClientFactory.CreateClient(nameof(AndroidFileSaver));
            using var response = await client.GetAsync(sUri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (fileName.IsNullOrEmpty())
                fileName = GetResponseFileName(response) ?? "download";
            var inputStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using var _1 = inputStream.ConfigureAwait(false);
            succeeded = await SaveImageToGallery(inputStream, fileName, contentType).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to save media. ContentType: '{ContentType}', Uri: '{Uri}'", contentType, sUri);
        }
        return succeeded;
    }

    private static string? GetResponseFileName(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        return (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"').NullIfEmpty();
    }

    private async Task<bool> SaveImageToGallery(Stream inputStream, string fileName, string contentType)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Q)
            return await SaveImageToGalleryCompat(inputStream, fileName, contentType).ConfigureAwait(false);

        var contentKind = GetContentKind(contentType);
        var contentValues = new ContentValues();
        var dirDest = new File(GetSubDirectoryForContentKind(contentKind), AppSubFolder);
        contentValues.Put(MediaStore.IMediaColumns.RelativePath, dirDest + File.Separator);
        contentValues.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        contentValues.Put(MediaStore.IMediaColumns.MimeType, contentType);

        var uriToInsert = contentKind switch {
            ContentKind.Image => MediaStore.Images.Media.GetContentUri(MediaStore.VolumeExternalPrimary)!,
            ContentKind.Video => MediaStore.Video.Media.GetContentUri(MediaStore.VolumeExternalPrimary)!,
            ContentKind.Audio => MediaStore.Audio.Media.GetContentUri(MediaStore.VolumeExternalPrimary)!,
            _ => MediaStore.Downloads.GetContentUri(MediaStore.VolumeExternalPrimary)
        };
        var context = Platform.AppContext;
        var contentResolver = context.ContentResolver!;
        var dstUri = contentResolver.Insert(uriToInsert, contentValues);

        if (dstUri == null) {
            Log.LogError("Failed to save media file");
            return false;
        }

        try {
            var outputStream = contentResolver.OpenOutputStream(dstUri)!;
            await using var _1 = outputStream.ConfigureAwait(false);
            await inputStream.CopyToAsync(outputStream).ConfigureAwait(false);
            Log.LogDebug("File saved to the gallery: {FileName}", fileName);
            return true;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to save file to the gallery");
            return false;
        }
    }

    private static ContentKind GetContentKind(string contentType)
        => contentType switch {
            _ when contentType.StartsWith("image/") => ContentKind.Image,
            _ when contentType.StartsWith("video/") => ContentKind.Video,
            _ when contentType.StartsWith("audio/") => ContentKind.Audio,
            _ => ContentKind.Other,
        };

    private static string GetSubDirectoryForContentKind(ContentKind contentKind)
    {
        var contentTypeSubDirectory = contentKind switch {
            ContentKind.Image => Environment.DirectoryPictures!,
            ContentKind.Video => Environment.DirectoryMovies!,
            ContentKind.Audio => Environment.DirectoryMusic!,
            _ => Environment.DirectoryDownloads!
        };
        return contentTypeSubDirectory;
    }

    private async Task<bool> SaveImageToGalleryCompat(Stream inputStream, string fileName, string contentType)
    {
        try {
            var activity = MainActivity.Current.Require();
            var writeStoragePermission = activity.CheckSelfPermission(Manifest.Permission.WriteExternalStorage);
            if (writeStoragePermission != Permission.Granted) {
                var completionSource = AsyncTaskMethodBuilderExt.New<bool>();
                activity.RequestPermission(Manifest.Permission.WriteExternalStorage, hasGranted1 => completionSource.TrySetResult(hasGranted1));
                var hasGranted = await completionSource.Task.ConfigureAwait(false);
                if (!hasGranted) {
                    Log.LogInformation("Permission to store files to external storage was not granted");
                    return false;
                }
            }

            var contentKind = GetContentKind(contentType);
            var directory = new File(Environment.GetExternalStoragePublicDirectory(GetSubDirectoryForContentKind(contentKind)), AppSubFolder);
            var directoryExists = directory.Exists();
            if (!directoryExists) {
                directoryExists = directory.Mkdirs();
                if (!directoryExists) {
                    Log.LogWarning("Failed to create directory '{Dir}'", directory.AbsolutePath);
                    return false;
                }
            }
            var filePath = Path.Combine(directory.AbsolutePath, fileName);
            if (System.IO.File.Exists(filePath))
                filePath = EnsureFilePathIsFree(directory.AbsolutePath, fileName);
            var outputStream = System.IO.File.OpenWrite(filePath);
            await using var _1 = outputStream.ConfigureAwait(false);
            await inputStream.CopyToAsync(outputStream).ConfigureAwait(false);
            Log.LogDebug("File saved to: '{FilePath}'", filePath);

            var contentValues = new ContentValues();
            contentValues.Put(MediaStore.IMediaColumns.Data, filePath);
            contentValues.Put(MediaStore.IMediaColumns.MimeType, contentType);
            var uriToInsert = contentKind switch {
                ContentKind.Image => MediaStore.Images.Media.ExternalContentUri!,
                ContentKind.Video => MediaStore.Video.Media.ExternalContentUri!,
                ContentKind.Audio => MediaStore.Audio.Media.ExternalContentUri!,
                _ => MediaStore.Downloads.ExternalContentUri
            };
            var contentResolver = activity.ContentResolver!;
            contentResolver.Insert(uriToInsert, contentValues);

            MediaScannerConnection.ScanFile(activity,
                [filePath],
                [contentType],
                new ScanCompletedListener(
                    (path, uri) => Log.LogDebug("Scanned '{Path}' -> uri='{Uri}'", path, uri)));

            return true;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to save file");
            return false;
        }
    }

    private static string EnsureFilePathIsFree(string directoryAbsolutePath, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

        for (int i = 1; i <= 20; i++) {
            var filePath = Path.Combine(directoryAbsolutePath, NewFileName(i));
            if (!System.IO.File.Exists(filePath))
                return filePath;
        }
        return Path.Combine(directoryAbsolutePath, NewFileName(System.Environment.TickCount64));

        string NewFileName(long index)
        {
            var newFileName = fileNameWithoutExtension + " (" + index.ToString() + ")";
            if (!extension.IsNullOrEmpty())
                newFileName += extension;
            return newFileName;
        }
    }

    // Nested types

    private enum ContentKind { Image, Video, Audio, Other }

    private class ScanCompletedListener : JObject, MediaScannerConnection.IOnScanCompletedListener
    {
        private readonly Action<string, Uri> _onScanCompleted;

        public ScanCompletedListener(Action<string, Uri> onScanCompleted)
            => _onScanCompleted = onScanCompleted;

        public void OnScanCompleted(string? path, Uri? uri)
            => _onScanCompleted(path!, uri!);
    }
}
