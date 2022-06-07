using Android.Webkit;
using Java.Interop;

namespace ActualChat.ClientApp;

partial class MauiBlazorWebViewHandler
{
    protected override Android.Webkit.WebView CreatePlatformView()
    {
        var webView = base.CreatePlatformView();
        webView.Settings.JavaScriptEnabled = true;
        var cookieManager = CookieManager.Instance!;
        // May be will be required https://stackoverflow.com/questions/2566485/webview-and-cookies-on-android
        //cookieManager.SetAcceptThirdPartyCookies(webView, true);
        var jsInterface = new JavascriptInterface(this, webView);
        webView.AddJavascriptInterface(jsInterface, "Android");
#pragma warning disable VSTHRD101 // Avoid unsupported async delegates
        jsInterface.MessageReceived += async json => {
            if (!string.IsNullOrWhiteSpace(json) && json.Length > 10 && json[0] == '{') {
                try {
                    var msg = System.Text.Json.JsonSerializer.Deserialize<JsMessage>(json);
                    if (msg == null)
                        return;
                    var sender = webView;
                    switch (msg.type) {
                    case "_auth":
                        //var uri = new Uri(msg.url.Replace("/fusion/close-app?", $"/fusion/close-app?port={GetRandomUnusedPort()}&", StringComparison.Ordinal));
                        var originalUri = sender.Url;
                        sender.LoadUrl(msg.url);
                        Debug.WriteLine($"_auth: {msg.url}");
                        var cookies = await GetRedirectSecret().ConfigureAwait(false);
                        foreach (var (key, value) in cookies) {
                            cookieManager.SetCookie("0.0.0.0", $"{key}={value}");
                            cookieManager.SetCookie(new Uri(BaseUri).Host, $"{key}={value}");

                            if (string.Equals(key, "FusionAuth.SessionId", StringComparison.Ordinal)) {
                                string path = Path.Combine(FileSystem.AppDataDirectory, "session.txt");
                                await File.WriteAllTextAsync(path, value).ConfigureAwait(true);
                            }
                        }
                        sender.LoadUrl(originalUri);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown message type: {msg.type}");
                    }
                }
                catch (Exception ex) {
                    Debug.WriteLine(ex.ToString());
                }
            }
        };
        return webView;
    }

    private class JavascriptInterface : Java.Lang.Object
    {
        private readonly MauiBlazorWebViewHandler _handler;
        private readonly Android.Webkit.WebView _webView;

        public event Action<string> MessageReceived = m => { };

        public JavascriptInterface(MauiBlazorWebViewHandler handler, Android.Webkit.WebView webView)
        {
            _handler = handler;
            _webView = webView;
        }

        [JavascriptInterface]
        [Export("DOMContentLoaded")]
        public void OnDOMContentLoaded()
        {
            _webView.Post(() => { 
                try {
                    var script = $"window.chatApp.initPage('{_handler.BaseUri}')";
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
        {
            _webView.Post(() => {
                MessageReceived.Invoke(data);
            });
        }
    }
}
