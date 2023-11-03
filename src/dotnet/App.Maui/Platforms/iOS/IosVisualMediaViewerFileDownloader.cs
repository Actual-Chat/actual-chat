using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Foundation;
using Stl.Fusion.UI;
using UIKit;

namespace ActualChat.App.Maui;

public class IosVisualMediaViewerFileDownloader(IServiceProvider services) : IVisualMediaViewerFileDownloader
{
    private HttpClient? _httpClient;
    private ToastUI? _toastUI;
    private UICommander? _uiCommander;
    private ILogger? _log;

    private HttpClient HttpClient => _httpClient ??= services.HttpClientFactory().CreateClient(GetType().Name);
    private ToastUI ToastUI => _toastUI ??= services.GetRequiredService<ToastUI>();
    private UICommander UICommander => _uiCommander ??= services.UICommander();
    private ILogger Log => _log ??= services.LogFor(GetType());

    public async Task Download(string uri)
    {
        try {
            // TODO: distinguish image and video
            var stream = await HttpClient.GetStreamAsync(uri).ConfigureAwait(false);
            await using var _ = stream.ConfigureAwait(false);
            var nsData = NSData.FromStream(stream) ?? throw StandardError.External("Could not load media.");
            var image = UIImage.LoadFromData(nsData) ?? throw StandardError.External("Could not load media.");
            await DispatchToBlazor(_ => image.SaveToPhotosAlbum(OnComplete)).ConfigureAwait(false);
        }
        catch (Exception e) {
            OnError(e);
        }
    }

    private void OnComplete(UIImage image, NSError? error)
    {
        if (error != null)
            OnError(new NSErrorException(error));
        else
            ToastUI.Show("Successfully saved media to library", "icon-checkmark-circle-2", ToastDismissDelay.Short);
    }

    private void OnError(Exception exception)
    {
        Log.LogError(exception, "Could not save image to photo library");
        UICommander.ShowError(exception);
    }
}
