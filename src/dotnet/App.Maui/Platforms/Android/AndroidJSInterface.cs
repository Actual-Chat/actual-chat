using ActualChat.UI.Blazor.Services;
using Android.Content;
using Android.Webkit;
using Java.Interop;

namespace ActualChat.App.Maui;

#pragma warning disable CA1822 // Can be static

internal class AndroidJSInterface(Android.Webkit.WebView webView) : Java.Lang.Object
{
    public event Action<string> MessageReceived = _ => { };

    [JavascriptInterface]
    [Export("DOMContentLoaded")]
    public void OnDOMContentLoaded()
        => LoadingUI.MarkViewCreated();

    [JavascriptInterface]
    [Export("postMessage")]
    public void OnPostMessage(string data)
        => webView.Post(() => {
            MessageReceived.Invoke(data);
        });

    [JavascriptInterface]
    [Export("writeTextToClipboard")]
    public void WriteTextToClipboard(string? newClipText)
    {
        var clipboard = (ClipboardManager)webView.Context!.GetSystemService(Context.ClipboardService)!;
        clipboard.Text = newClipText;
    }

    [JavascriptInterface]
    [Export("readTextFromClipboard")]
    public string? ReadTextFromClipboard()
    {
        var clipboard = (ClipboardManager)webView.Context!.GetSystemService(Context.ClipboardService)!;
        return clipboard.Text;
    }
}
