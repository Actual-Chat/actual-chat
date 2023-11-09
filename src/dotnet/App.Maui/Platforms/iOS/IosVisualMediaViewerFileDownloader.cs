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
            var stream = await HttpClient.GetStreamAsync(uri).ConfigureAwait(false);
            await using var _ = stream.ConfigureAwait(false);
            var nsData = NSData.FromStream(stream) ?? throw StandardError.External("Could not load media.");
            var granted = await PermissionHandler.CheckOrRequest(CancellationToken.None).ConfigureAwait(false);
            if (!granted)
                throw StandardError.Unauthorized("No permission to add photos to library");

            var type = GetResourceType();
            await DispatchToBlazor(_ => Save(type, nsData)).ConfigureAwait(false);
            ToastUI.Show("Successfully saved media to library", "icon-checkmark-circle-2", ToastDismissDelay.Short);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to save media to library");
            UICommander.ShowError(e);
        }
        return;

        PHAssetResourceType GetResourceType()
        {
            if (MediaTypeExt.IsSupportedVideo(contentType))
                return PHAssetResourceType.Video;

            if (MediaTypeExt.IsSupportedImage(contentType))
                return PHAssetResourceType.Photo;

            throw StandardError.Constraint("Could not save media to library: it's not a photo nor a video");
        }
    }

    private Task Save(PHAssetResourceType type, NSData data)
    {
        var tcs = new TaskCompletionSource();
        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(() => {
                var request = PHAssetCreationRequest.CreationRequestForAsset();
                request.AddResource(type, data, null);
            },
            (success, error) => {
                if (success)
                    tcs.SetResult();
                else {
                    Log.LogError(new NSErrorException(error), "Could not save media to photo library: {Error}", error);
                    tcs.SetException(StandardError.External("Could not save media to library."));
                }
            });
        return tcs.Task;
    }
}
