using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using Foundation;
using UIKit;

namespace ActualChat.App.Maui;

public class MacClipboardUI(UIHub hub) : MauiClipboardUI(hub)
{
    protected override async Task SetClipboardImage(Stream data)
    {
        using var nsData = NSData.FromStream(data);
        if (nsData == null)
            return;

        using var image = UIImage.LoadFromData(nsData);
        if (image is null)
            return;

        // ReSharper disable once AccessToDisposedClosure
        await MainThread.InvokeOnMainThreadAsync(() => UIPasteboard.General.Image = image).ConfigureAwait(false);
    }
}
