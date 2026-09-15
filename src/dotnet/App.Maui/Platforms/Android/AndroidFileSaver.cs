using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Localization;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Webkit;
using Environment = Android.OS.Environment;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

public sealed class AndroidFileSaver(IServiceProvider services)
    : IFileSaver
{
    private const string AppSubFolder = CoreConstants.AppName;
    // Shared storage rejects these, and '/' would turn the name into a path
    private static readonly char[] InvalidFileNameChars = ['"', '*', '/', ':', '<', '>', '?', '\\', '|'];

    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _downloadIds = new();

    private IServiceProvider Services { get; } = services;
    private ToastUI ToastUI => field ??= Services.GetRequiredService<ToastUI>();
    private IStringLocalizer L => field ??= Services.GetRequiredService<IStringLocalizer>();
    private DownloadManager DownloadManager
        => field ??= (DownloadManager)Platform.AppContext.GetSystemService(Context.DownloadService)!;
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task Save(IReadOnlyList<FileToSave> files)
    {
        if (files.Count == 0)
            return;

        var hasPermission = OperatingSystem.IsAndroidVersionAtLeast(29)
            || await RequestWriteStoragePermission().ConfigureAwait(true);
        var results = hasPermission ? files.Select(f => (File: f, Result: Enqueue(f))).ToList() : [];
        var enqueued = results.Where(x => x.Result == EnqueueResult.Enqueued).Select(x => x.File).ToList();
        if (enqueued.Count != 0)
            ToastUI.Show(GetDownloadingText(enqueued), "icon-download", ToastDismissDelay.Short);
        else if (results.Any(x => x.Result == EnqueueResult.InProgress))
            ToastUI.Show(L.FileSaver_AlreadyDownloading(files.Count), "icon-download", ToastDismissDelay.Short);
        else
            ToastUI.Show(L.FileSaver_SaveFailed(files.Count), "icon-alert-circle", ToastDismissDelay.Long);
    }

    // Private methods

    private async Task<bool> RequestWriteStoragePermission()
    {
        var activity = MainActivity.Current.Require();
        if (activity.CheckSelfPermission(Manifest.Permission.WriteExternalStorage) == Permission.Granted)
            return true;

        var completionSource = AsyncTaskMethodBuilderExt.New<bool>();
        activity.RequestPermission(Manifest.Permission.WriteExternalStorage,
            hasGranted1 => completionSource.TrySetResult(hasGranted1));
        var hasGranted = await completionSource.Task.ConfigureAwait(true);
        if (!hasGranted)
            Log.LogInformation("Permission to store files to external storage was not granted");
        return hasGranted;
    }

    private EnqueueResult Enqueue(FileToSave file)
    {
        try {
            lock (_lock) {
                if (_downloadIds.TryGetValue(file.Url, out var downloadId) && IsInProgress(downloadId))
                    return EnqueueResult.InProgress;

                _downloadIds[file.Url] = DownloadManager.Enqueue(NewRequest(file));
            }
            return EnqueueResult.Enqueued;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to enqueue download. ContentType: '{ContentType}', Uri: '{Uri}'",
                file.ContentType, file.Url);
            return EnqueueResult.Failed;
        }
    }

    private bool IsInProgress(long downloadId)
    {
        using var cursor = DownloadManager.InvokeQuery(new DownloadManager.Query().SetFilterById(downloadId)!);
        if (cursor is null || !cursor.MoveToFirst())
            return false;

        var status = (DownloadStatus)cursor.GetInt(cursor.GetColumnIndexOrThrow(DownloadManager.ColumnStatus));
        return status is DownloadStatus.Pending or DownloadStatus.Running or DownloadStatus.Paused;
    }

    private static DownloadManager.Request NewRequest(FileToSave file)
    {
        var fileName = GetFileName(file);
        var request = new DownloadManager.Request(Uri.Parse(file.Url)!);
        request.SetTitle(fileName);
        request.SetMimeType(file.ContentType);
        request.SetNotificationVisibility(DownloadVisibility.VisibleNotifyCompleted);
        request.SetDestinationInExternalPublicDir(GetDirectory(file.ContentType), $"{AppSubFolder}/{fileName}");
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            request.AllowScanningByMediaScanner();
        return request;
    }

    private string GetDownloadingText(IReadOnlyList<FileToSave> files)
    {
        // A mixed group lands in several places at once, so the toast names none of them.
        var count = files.Count;
        var targets = files.Select(f => GetTarget(f.ContentType)).Distinct().ToList();
        if (targets.Count != 1)
            return L.FileSaver_Downloading(count, count);

        return targets[0] switch {
            SaveTarget.Gallery => L.FileSaver_DownloadingToGallery(count, count),
            SaveTarget.Music => L.FileSaver_DownloadingToMusic(count, count),
            _ => L.FileSaver_DownloadingToDownloads(count, count),
        };
    }

    private static string GetFileName(FileToSave file)
    {
        var fileName = file.FileName;
        if (fileName.IsNullOrEmpty()) {
            var extension = MimeTypeMap.Singleton?.GetExtensionFromMimeType(file.ContentType);
            fileName = extension.IsNullOrEmpty() ? "download" : "download." + extension;
        }
        return string.Concat(fileName.Select(c => InvalidFileNameChars.Contains(c) || char.IsControl(c) ? '_' : c));
    }

    private static SaveTarget GetTarget(string contentType)
        => GetContentKind(contentType) switch {
            ContentKind.Image or ContentKind.Video => SaveTarget.Gallery,
            ContentKind.Audio => SaveTarget.Music,
            _ => SaveTarget.Downloads,
        };

    private static string GetDirectory(string contentType)
        => GetContentKind(contentType) switch {
            ContentKind.Image => Environment.DirectoryPictures!,
            ContentKind.Video => Environment.DirectoryMovies!,
            ContentKind.Audio => Environment.DirectoryMusic!,
            _ => Environment.DirectoryDownloads!,
        };

    private static ContentKind GetContentKind(string contentType)
        => contentType switch {
            _ when contentType.StartsWith("image/") => ContentKind.Image,
            _ when contentType.StartsWith("video/") => ContentKind.Video,
            _ when contentType.StartsWith("audio/") => ContentKind.Audio,
            _ => ContentKind.Other,
        };

    // Nested types

    private enum ContentKind { Image, Video, Audio, Other }

    private enum SaveTarget { Gallery, Music, Downloads }

    private enum EnqueueResult { Enqueued, InProgress, Failed }
}
