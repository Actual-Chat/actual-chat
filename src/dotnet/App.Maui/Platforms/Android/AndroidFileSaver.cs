using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;
using ActualLab.IO;
using Microsoft.Extensions.Localization;
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

public sealed class AndroidFileSaver(IServiceProvider services)
    : IFileSaver
{
    private const string AppSubFolder = CoreConstants.AppName;

    private IServiceProvider Services { get; } = services;
    private ToastUI ToastUI => field ??= Services.GetRequiredService<ToastUI>();
    private IHttpClientFactory HttpClientFactory => field ??= Services.GetRequiredService<IHttpClientFactory>();
    private IStringLocalizer L => field ??= Services.GetRequiredService<IStringLocalizer>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task Save(IReadOnlyList<FileToSave> files)
    {
        if (files.Count == 0)
            return;

        // TODO(DF): Add special handling to ensure reliable file loading.
        // Provide visual feedback for long loading files.
        var savedCount = await BackgroundTask
            .Run(() => SaveAll(files), CancellationToken.None)
            .ConfigureAwait(true);

        if (savedCount == 0)
            ToastUI.Show(L.FileSaver_SaveFailed(files.Count), "icon-alert-circle", ToastDismissDelay.Long);
        else if (savedCount < files.Count)
            ToastUI.Show(L.FileSaver_PartiallySaved(savedCount, savedCount, files.Count),
                "icon-alert-circle", ToastDismissDelay.Long);
        else
            ToastUI.Show(GetSavedText(files, savedCount), "icon-checkmark-circle-2", ToastDismissDelay.Short);
    }

    // Private methods

    private async Task<int> SaveAll(IReadOnlyList<FileToSave> files)
    {
        var savedCount = 0;
        foreach (var file in files) {
            if (await SaveOne(file).ConfigureAwait(false))
                savedCount++;
        }

        return savedCount;
    }

    private async Task<bool> SaveOne(FileToSave file)
    {
        var isSaved = false;
        try {
            using var client = HttpClientFactory.CreateClient(nameof(AndroidFileSaver));
            using var response = await client
                .GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var fileName = file.FileName.IsNullOrEmpty()
                ? GetResponseFileName(response) ?? "download"
                : file.FileName;
            var inputStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using var _1 = inputStream.ConfigureAwait(false);
            isSaved = await SaveImageToGallery(inputStream, fileName, file.ContentType).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to save media. ContentType: '{ContentType}', Uri: '{Uri}'",
                file.ContentType, file.Url);
        }

        return isSaved;
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

    private async Task<bool> SaveImageToGalleryCompat(Stream inputStream, string fileName, string contentType)
    {
        try {
            var activity = MainActivity.Current.Require();
            var writeStoragePermission = activity.CheckSelfPermission(Manifest.Permission.WriteExternalStorage);
            if (writeStoragePermission != Permission.Granted) {
                var completionSource = AsyncTaskMethodBuilderExt.New<bool>();
                activity.RequestPermission(Manifest.Permission.WriteExternalStorage,
                    hasGranted1 => completionSource.TrySetResult(hasGranted1));
                var hasGranted = await completionSource.Task.ConfigureAwait(false);
                if (!hasGranted) {
                    Log.LogInformation("Permission to store files to external storage was not granted");
                    return false;
                }
            }

            var contentKind = GetContentKind(contentType);
            var subDirectory = GetSubDirectoryForContentKind(contentKind);
            var directory = new File(Environment.GetExternalStoragePublicDirectory(subDirectory), AppSubFolder);
            var hasDirectory = directory.Exists();
            if (!hasDirectory) {
                hasDirectory = directory.Mkdirs();
                if (!hasDirectory) {
                    Log.LogWarning("Failed to create directory '{Dir}'", directory.AbsolutePath);
                    return false;
                }
            }

            var directoryPath = (FilePath)directory.AbsolutePath;
            var filePath = directoryPath & fileName;
            if (System.IO.File.Exists(filePath))
                filePath = EnsureFilePathIsFree(directoryPath, fileName);

            var outputStream = System.IO.File.OpenWrite(filePath);
            await using var _1 = outputStream.ConfigureAwait(false);
            await inputStream.CopyToAsync(outputStream).ConfigureAwait(false);
            Log.LogDebug("File saved to: '{FilePath}'", filePath);

            var contentValues = new ContentValues();
            contentValues.Put(MediaStore.IMediaColumns.Data, filePath.Value);
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
                [filePath.Value],
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

    private static string? GetResponseFileName(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        return (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"').NullIfEmpty();
    }

    private string GetSavedText(IReadOnlyList<FileToSave> files, int savedCount)
    {
        // A mixed group lands in several places at once, so the toast names none of them.
        var targets = files.Select(f => GetTarget(f.ContentType)).Distinct().ToList();
        if (targets.Count != 1)
            return L.FileSaver_Saved(savedCount, savedCount);

        return targets[0] switch {
            SaveTarget.Gallery => L.FileSaver_SavedToGallery(savedCount, savedCount),
            SaveTarget.Music => L.FileSaver_SavedToMusic(savedCount, savedCount),
            _ => L.FileSaver_SavedToDownloads(savedCount, savedCount),
        };
    }

    private static SaveTarget GetTarget(string contentType)
        => GetContentKind(contentType) switch {
            ContentKind.Image or ContentKind.Video => SaveTarget.Gallery,
            ContentKind.Audio => SaveTarget.Music,
            _ => SaveTarget.Downloads,
        };

    private static ContentKind GetContentKind(string contentType)
        => contentType switch {
            _ when contentType.StartsWith("image/") => ContentKind.Image,
            _ when contentType.StartsWith("video/") => ContentKind.Video,
            _ when contentType.StartsWith("audio/") => ContentKind.Audio,
            _ => ContentKind.Other,
        };

    private static string GetSubDirectoryForContentKind(ContentKind contentKind)
        => contentKind switch {
            ContentKind.Image => Environment.DirectoryPictures!,
            ContentKind.Video => Environment.DirectoryMovies!,
            ContentKind.Audio => Environment.DirectoryMusic!,
            _ => Environment.DirectoryDownloads!,
        };

    private static FilePath EnsureFilePathIsFree(FilePath directoryPath, FilePath fileName)
    {
        var extension = fileName.Extension;
        var fileNameWithoutExtension = fileName.FileNameWithoutExtension;

        for (var i = 1; i <= 20; i++) {
            var filePath = directoryPath & NewFileName(i);
            if (!System.IO.File.Exists(filePath))
                return filePath;
        }

        return directoryPath & NewFileName(System.Environment.TickCount64);

        FilePath NewFileName(long index) {
            var newFileName = fileNameWithoutExtension + " (" + index + ")";
            if (!extension.IsNullOrEmpty())
                newFileName += extension;

            return newFileName;
        }
    }

    // Nested types

    private enum ContentKind { Image, Video, Audio, Other }

    private enum SaveTarget { Gallery, Music, Downloads }

    private sealed class ScanCompletedListener(Action<string, Uri> onScanCompleted)
        : JObject, MediaScannerConnection.IOnScanCompletedListener
    {
        public void OnScanCompleted(string? path, Uri? uri)
            => onScanCompleted(path!, uri!);
    }
}
