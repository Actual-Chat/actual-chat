using ActualChat.Localization;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using ActualLab.Generators;
using ActualLab.IO;
using Foundation;
using Photos;
using UIKit;
using DataTransfer = Microsoft.Maui.ApplicationModel.DataTransfer;

namespace ActualChat.App.Maui;

public sealed class AppleFileSaver(UIHub hub) : UIServiceBase<UIHub>(hub), IFileSaver
{
    private HttpClient HttpClient
        => field ??= Hub.Services.HttpClientFactory().CreateClient(GetType().Name);
    private AddPhotoPermissionHandler PermissionHandler
        => field ??= Hub.Services.GetRequiredService<AddPhotoPermissionHandler>();

    public async Task Save(IReadOnlyList<FileToSave> files)
    {
        if (files.Count == 0)
            return;

        try {
            // There's no shared Downloads location on iOS - anything that isn't gallery
            // media goes to the share sheet, where "Save to Files" is the user's save.
            var media = files.Where(f => MediaTypeExt.IsSupportedVisualMedia(f.ContentType)).ToList();
            var others = files.Where(f => !MediaTypeExt.IsSupportedVisualMedia(f.ContentType)).ToList();
            if (media.Count != 0)
                await SaveToLibrary(media).ConfigureAwait(false);
            if (others.Count != 0)
                await SaveViaShareSheet(others).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to save media to library");
            UICommander.ShowError(e);
        }
    }

    // Private methods

    private async Task SaveToLibrary(IReadOnlyList<FileToSave> files)
    {
        var isGranted = await PermissionHandler.CheckOrRequest(CancellationToken.None).ConfigureAwait(false);
        if (!isGranted)
            throw StandardError.Unauthorized("No permission to add photos/videos to library");

        foreach (var file in files) {
            var tempFilePath = await DownloadToTempFile(file).ConfigureAwait(false);
            await DispatchToBlazor(_ => Save(tempFilePath, GetResourceType(file.ContentType))).ConfigureAwait(false);
        }

        ToastUI.Show(L.FileSaver_SavedToLibrary(files.Count, files.Count),
            "icon-checkmark-circle-2", ToastDismissDelay.Short);
    }

    private async Task SaveViaShareSheet(IReadOnlyList<FileToSave> files)
    {
        var shareFiles = new List<DataTransfer.ShareFile>(files.Count);
        foreach (var file in files)
            shareFiles.Add(new DataTransfer.ShareFile(await DownloadToTempFile(file).ConfigureAwait(false)));

        // The share sheet is a UIViewController presentation, so it must happen on the main
        // thread - off it, iOS drops it silently. The await above lands us on a pool thread.
        await MainThread.InvokeOnMainThreadAsync(
            () => DataTransfer.Share.Default.RequestAsync(new DataTransfer.ShareMultipleFilesRequest {
                Title = files.Count == 1 ? files[0].FileName : L.Editor_Files(files.Count, files.Count),
                Files = shareFiles,
            })).ConfigureAwait(false);
    }

    private Task Save(FilePath tempFilePath, PHAssetResourceType type)
    {
        var completedSource = AsyncTaskMethodBuilderExt.New();
        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(
            () => {
                switch (type) {
                case PHAssetResourceType.Photo:
                    var uiImage = UIImage.FromFile(tempFilePath);
                    PHAssetChangeRequest.FromImage(uiImage!);
                    break;
                case PHAssetResourceType.Video:
                    var nsUrl = NSUrl.FromFilename(tempFilePath);
                    PHAssetChangeRequest.FromVideo(nsUrl);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
                }
            },
            (success, error) => {
                File.Delete(tempFilePath);
                if (success)
                    completedSource.SetResult();
                else {
                    Log.LogError(new NSErrorException(error), "Could not save media to photo library: {Error}", error);
                    completedSource.SetException(StandardError.External("Could not save media to library."));
                }
            });
        return completedSource.Task;
    }

    private async Task<FilePath> DownloadToTempFile(FileToSave file)
    {
        var response = await HttpClient.GetAsync(file.Url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);

        // A per-download subfolder keeps the real file name - which the share sheet shows and
        // "Save to Files" reuses - without colliding with earlier downloads.
        var cacheDirectory = (FilePath)FileSystem.Current.CacheDirectory;
        var downloadsFolder = Directory.CreateDirectory(
            cacheDirectory & "downloads" & RandomStringGenerator.Default.Next());
        var tempFilePath = (FilePath)downloadsFolder.FullName & GetFileName(file.FileName, file.ContentType);
        var fs = File.OpenWrite(tempFilePath);
        await using var __ = fs.ConfigureAwait(false);
        await stream.CopyToAsync(fs).ConfigureAwait(false);

        return tempFilePath;
    }

    private static FilePath GetFileName(string fileName, string contentType)
    {
        if (!fileName.IsNullOrEmpty())
            return fileName;

        var extension = MediaTypeExt.GetFileExtension(contentType)
            ?? throw StandardError.Constraint("Not supported media type.");
        return "download" + extension;
    }

    private static PHAssetResourceType GetResourceType(string contentType)
    {
        if (MediaTypeExt.IsSupportedVideo(contentType))
            return PHAssetResourceType.Video;
        if (MediaTypeExt.IsSupportedImage(contentType))
            return PHAssetResourceType.Photo;

        throw StandardError.Constraint("Could not save media to library: it's not a photo nor a video");
    }
}
