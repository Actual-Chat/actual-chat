using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using Windows.Storage.Streams;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using WinDataPackage = Windows.ApplicationModel.DataTransfer.DataPackage;

namespace ActualChat.App.Maui;

public class WindowsClipboardUI(UIHub hub) : MauiClipboardUI(hub)
{
    protected override async Task SetClipboardImage(Stream data)
    {
        var stream = new InMemoryRandomAccessStream();
        await data.CopyToAsync(stream.AsStreamForWrite()).ConfigureAwait(false);
        stream.Seek(0);

        await MainThread.InvokeOnMainThreadAsync(() => {
            var package = new WinDataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            WinClipboard.SetContent(package);
        }).ConfigureAwait(false);
    }
}
