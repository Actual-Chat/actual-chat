using ActualChat.Chat;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Foundation;
using Photos;
using Stl.Fusion.UI;

namespace ActualChat.App.Maui;

public class IosVisualMediaViewerFileDownloader(IServiceProvider services) : IVisualMediaViewerFileDownloader
{
    private HttpClient? _httpClient;
    private AddPhotoPermissionHandler? _permissionHandler;
    private ToastUI? _toastUI;
    private UICommander? _uiCommander;
    private ILogger? _log;

    private HttpClient HttpClient => _httpClient ??= services.HttpClientFactory().CreateClient(GetType().Name);
    private AddPhotoPermissionHandler PermissionHandler => _permissionHandler ??= services.GetRequiredService<AddPhotoPermissionHandler>();
    private ToastUI ToastUI => _toastUI ??= services.GetRequiredService<ToastUI>();
    private UICommander UICommander => _uiCommander ??= services.UICommander();
    private ILogger Log => _log ??= services.LogFor(GetType());

    public async Task Download(string uri, string contentType)
    {
        try {
            var granted = await PermissionHandler.CheckOrRequest(CancellationToken.None).ConfigureAwait(false);
            if (!granted)
                throw StandardError.Unauthorized("No permission to add photos/videos to library");

            var (tempFilePath, type) = await DownloadToTempFile(uri, contentType).ConfigureAwait(false);
            await DispatchToBlazor(_ => Save(tempFilePath, type)).ConfigureAwait(false);
            ToastUI.Show("Successfully saved media to library", "icon-checkmark-circle-2", ToastDismissDelay.Short);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to save media to library");
            UICommander.ShowError(e);
        }
    }

    private Task Save(string tempFilePath, PHAssetResourceType type)
    {
        var tcs = new TaskCompletionSource();
        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(
            () => {
                var nsUrl = NSUrl.FromFilename(tempFilePath);
                switch (type) {
                case PHAssetResourceType.Photo:
                    PHAssetChangeRequest.FromImage(nsUrl);
                    break;
                case PHAssetResourceType.Video:
                    PHAssetChangeRequest.FromVideo(nsUrl);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
                }
            },
            (success, error) => {
                File.Delete(tempFilePath);
                if (success)
                    tcs.SetResult();
                else {
                    Log.LogError(new NSErrorException(error), "Could not save media to photo library: {Error}", error);
                    tcs.SetException(StandardError.External("Could not save media to library."));
                }
            });
        return tcs.Task;
    }

    private async Task<(string tempFilePath, PHAssetResourceType)> DownloadToTempFile(string url, string contentType)
    {
        var response = await HttpClient.GetAsync(url).ConfigureAwait(false);
        var ext = GetFileExtension();
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        var downloadsFolder = Directory.CreateDirectory(Path.Combine(FileSystem.Current.CacheDirectory, "downloads"));
        var tempFilePath = Path.Combine(downloadsFolder.FullName, Guid.NewGuid().ToString("N") + ext);
        var fs = File.OpenWrite(tempFilePath);
        await using var __ = fs.ConfigureAwait(false);
        await stream.CopyToAsync(fs).ConfigureAwait(false);
        return (tempFilePath, GetResourceType());

        string GetFileExtension()
        {
            var fileName = response.Content.Headers.ContentDisposition?.FileName;
            if (!fileName.IsNullOrEmpty())
                return Path.GetExtension(fileName);

            return MediaTypeExt.GetFileExtension(contentType)
                ?? throw StandardError.Constraint("Not supported media type.");
        }

        PHAssetResourceType GetResourceType()
        {
            if (MediaTypeExt.IsSupportedVideo(contentType))
                return PHAssetResourceType.Video;

            if (MediaTypeExt.IsSupportedImage(contentType))
                return PHAssetResourceType.Photo;

            throw StandardError.Constraint("Could not save media to library: it's not a photo nor a video");
        }
    }
}
