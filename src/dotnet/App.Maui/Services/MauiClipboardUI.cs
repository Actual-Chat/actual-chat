using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

public abstract class MauiClipboardUI(UIHub hub) : ClipboardUI(hub)
{
    private HttpClient HttpClient
        => field ??= Services.HttpClientFactory().CreateClient(nameof(ClipboardUI));

    public override bool CanWriteImage => true;

    public override async Task WriteImage(string uri)
    {
        try {
            var response = await HttpClient.GetAsync(uri, Hub.StopToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            // TODO: think of optimizing footprint
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            await SetClipboardImage(bytes).ConfigureAwait(false);
            ToastUI.Show("Image copied to clipboard", "icon-checkmark-circle-2", ToastDismissDelay.Short);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to copy image to clipboard");
            UICommander.ShowError(e);
        }
    }

    protected abstract Task SetClipboardImage(byte[] data);
}
