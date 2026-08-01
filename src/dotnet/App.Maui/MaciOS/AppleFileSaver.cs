using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Foundation;
using Photos;
using UIKit;
using DataTransfer = Microsoft.Maui.ApplicationModel.DataTransfer;

namespace ActualChat.App.Maui;

public class AppleFileSaver(UIHub hub) : UIServiceBase<UIHub>(hub), IFileSaver
{
    private HttpClient HttpClient
        => field ??= Hub.Services.HttpClientFactory().CreateClient(GetType().Name);
    private AddPhotoPermissionHandler PermissionHandler
        => field ??= Hub.Services.GetRequiredService<AddPhotoPermissionHandler>();

    public async Task Save(string url, string fileName, string contentType)
    {
        try {
            // There's no shared Downloads location on iOS - anything that isn't gallery
            // media goes to the share sheet, where "Save to Files" is the user's save.
            if (!MediaTypeExt.IsSupportedVisualMedia(contentType)) {
                await SaveViaShareSheet(url, fileName, contentType).ConfigureAwait(false);
                return;
            }

            var granted = await PermissionHandler.CheckOrRequest(CancellationToken.None).ConfigureAwait(false);
            if (!granted)
                throw StandardError.Unauthorized("No permission to add photos/videos to library");

            var tempFilePath = await DownloadToTempFile(url, fileName, contentType).ConfigureAwait(false);
            await DispatchToBlazor(_ => Save(tempFilePath, GetResourceType(contentType))).ConfigureAwait(false);
            ToastUI.Show("Successfully saved media to library", "icon-checkmark-circle-2", ToastDismissDelay.Short);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to save media to library");
            UICommander.ShowError(e);
        }
    }

    // Private methods

    private async Task SaveViaShareSheet(string url, string fileName, string contentType)
    {
        var tempFilePath = await DownloadToTempFile(url, fileName, contentType).ConfigureAwait(false);
        await DataTransfer.Share.Default.RequestAsync(new DataTransfer.ShareFileRequest {
            Title = fileName,
            File = new DataTransfer.ShareFile(tempFilePath),
        }).ConfigureAwait(false);
    }

    private Task Save(string tempFilePath, PHAssetResourceType type)
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

    private async Task<string> DownloadToTempFile(string url, string fileName, string contentType)
    {
        var response = await HttpClient.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);

        // A per-download subfolder keeps the real file name - which the share sheet shows and
        // "Save to Files" reuses - without colliding with earlier downloads.
        var downloadsFolder = Directory.CreateDirectory(Path.Combine(
            FileSystem.Current.CacheDirectory,
            "downloads",
            Guid.NewGuid().ToString("N")));
        var tempFilePath = Path.Combine(downloadsFolder.FullName, GetFileName(fileName, contentType));
        var fs = File.OpenWrite(tempFilePath);
        await using var __ = fs.ConfigureAwait(false);
        await stream.CopyToAsync(fs).ConfigureAwait(false);
        return tempFilePath;
    }

    private static string GetFileName(string fileName, string contentType)
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
