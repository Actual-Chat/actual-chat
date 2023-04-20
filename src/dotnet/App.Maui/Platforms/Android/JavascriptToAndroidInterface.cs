using Android.Content;
using Android.Webkit;
using Java.Interop;

namespace ActualChat.App.Maui;

internal class JavascriptToAndroidInterface : Java.Lang.Object
{
    private static readonly Tracer _tracer = Tracer.Default[nameof(JavascriptToAndroidInterface)];
    private readonly MauiBlazorWebViewHandler _handler;
    private readonly Android.Webkit.WebView _webView;

    public event Action<string> MessageReceived = m => { };

    public JavascriptToAndroidInterface(MauiBlazorWebViewHandler handler, Android.Webkit.WebView webView)
    {
        _handler = handler;
        _webView = webView;
    }

    [JavascriptInterface]
    [Export("DOMContentLoaded")]
    public void OnDOMContentLoaded()
    {
        _tracer.Point(nameof(OnDOMContentLoaded));
        _webView.Post(() => {
            try {
                _tracer.Point($"{nameof(OnDOMContentLoaded)} - window.App.initPage JS call");
                var sessionHash = new Session(_handler.AppSettings.SessionId).Hash;
                var script = $"window.App.initPage('{_handler.AppSettings.BaseUrl}', '{sessionHash}')";
                _webView.EvaluateJavascript(script, null);
            }
            catch (Exception ex) {
                Debug.WriteLine(ex.ToString());
            }
        });
    }

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
