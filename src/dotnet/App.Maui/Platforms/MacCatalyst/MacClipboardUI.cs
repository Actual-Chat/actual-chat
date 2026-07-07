using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using Foundation;
using UIKit;

namespace ActualChat.App.Maui;

public class MacClipboardUI(UIHub hub) : MauiClipboardUI(hub)
{
    protected override Task SetClipboardImage(byte[] data)
        => MainThread.InvokeOnMainThreadAsync(() => {
            using var nsData = NSData.FromArray(data);
            var image = UIImage.LoadFromData(nsData);
            if (image != null)
                UIPasteboard.General.Image = image;
        });
}
