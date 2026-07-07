using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using Windows.Storage.Streams;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using WinDataPackage = Windows.ApplicationModel.DataTransfer.DataPackage;

namespace ActualChat.App.Maui;

public class WindowsClipboardUI(UIHub hub) : MauiClipboardUI(hub)
{
    protected override Task SetClipboardImage(byte[] data)
        => MainThread.InvokeOnMainThreadAsync(async () => {
            var stream = new InMemoryRandomAccessStream();
            var writer = new DataWriter(stream);
            writer.WriteBytes(data);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            stream.Seek(0);

            var package = new WinDataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            WinClipboard.SetContent(package);
        });
}
