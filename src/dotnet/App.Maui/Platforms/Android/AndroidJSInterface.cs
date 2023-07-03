using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using Android.Webkit;
using Java.Interop;

namespace ActualChat.App.Maui;

internal class AndroidJSInterface : Java.Lang.Object
{
    private static readonly Tracer _tracer = Tracer.Default[nameof(AndroidJSInterface)];
    private readonly MauiBlazorWebViewHandler _handler;
    private readonly Android.Webkit.WebView _webView;

    public event Action<string> MessageReceived = _ => { };

    public AndroidJSInterface(MauiBlazorWebViewHandler handler, Android.Webkit.WebView webView)
    {
        _handler = handler;
        _webView = webView;
    }

    [JavascriptInterface]
    [Export("DOMContentLoaded")]
    public void OnDOMContentLoaded()
        => LoadingUI.MarkViewCreated();

    [JavascriptInterface]
    [Export("postMessage")]
    public void OnPostMessage(string data)
        => _webView.Post(() => {
            MessageReceived.Invoke(data);
        });

    [JavascriptInterface]
    [Export("writeTextToClipboard")]
    public void WriteTextToClipboard(string? newClipText)
    {
        var clipboard = (ClipboardManager)_webView.Context!.GetSystemService(Context.ClipboardService)!;
        clipboard.Text = newClipText;
    }

    [JavascriptInterface]
    [Export("readTextFromClipboard")]
    public string? ReadTextFromClipboard()
    {
        var clipboard = (ClipboardManager)_webView.Context!.GetSystemService(Context.ClipboardService)!;
        return clipboard.Text;
    }
}
