using ActualChat.App.Maui.Services;
using ActualChat.Localization;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using ActualLab.IO;
using Foundation;
using Photos;
using UIKit;

namespace ActualChat.App.Maui;

public sealed class AppleFileSaver(UIHub hub) : UIServiceBase<UIHub>(hub), IFileSaver
{
    private MauiTempFileDownloader TempFileDownloader
        => field ??= new MauiTempFileDownloader(Hub.Services);
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
            var tempFilePath = await Download(file).ConfigureAwait(false);
            await Save(tempFilePath, GetResourceType(file.ContentType)).ConfigureAwait(false);
        }

        ToastUI.Show(L.FileSaver_SavedToLibrary(files.Count, files.Count),
            "icon-checkmark-circle-2", ToastDismissDelay.Short);
    }

    private async Task SaveViaShareSheet(IReadOnlyList<FileToSave> files)
    {
        var shareFiles = new List<ShareFile>(files.Count);
        foreach (var file in files)
            shareFiles.Add(new ShareFile(await Download(file).ConfigureAwait(false)));

        // The share sheet is a UIViewController presentation, so it must happen on the main
        // thread - off it, iOS drops it silently. The await above lands us on a pool thread.
        await MainThread.InvokeOnMainThreadAsync(
            () => Share.Default.RequestAsync(new ShareMultipleFilesRequest {
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

    private Task<FilePath> Download(FileToSave file)
        => TempFileDownloader.Download(file.Url, file.FileName, file.ContentType, null, Hub.StopToken);

    private static PHAssetResourceType GetResourceType(string contentType)
    {
        if (MediaTypeExt.IsSupportedVideo(contentType))
            return PHAssetResourceType.Video;
        if (MediaTypeExt.IsSupportedImage(contentType))
            return PHAssetResourceType.Photo;

        throw StandardError.Constraint("Could not save media to library: it's not a photo nor a video");
    }
}
