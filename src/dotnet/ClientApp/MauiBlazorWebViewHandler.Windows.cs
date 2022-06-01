using System.Net;
using System.Text;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.ClientApp;

public partial class MauiBlazorWebViewHandler
{
    private const string loopbackUrl = "http://127.0.0.1:57348/";

    private record JsMessage(string type, string url);
    /// <inheritdoc />
    protected override WebView2Control CreatePlatformView()
    {
        var settings = MauiContext!.Services.GetRequiredService<ClientAppSettings>();
        var webview = new WebView2Control();
        var baseUri = settings.BaseUri;
        if (!settings.BaseUri.EndsWith('/'))
            baseUri += '/';
        webview.EnsureCoreWebView2Async().AsTask().ContinueWith((t, state) => {
            var ctrl = (WebView2Control)state!;
#pragma warning disable VSTHRD101 // Avoid unsupported async delegates
            ctrl.WebMessageReceived += async (WebView2Control sender, CoreWebView2WebMessageReceivedEventArgs args) => {
                var json = args.TryGetWebMessageAsString();
                if (!string.IsNullOrWhiteSpace(json) && json.Length > 10 && json[0] == '{') {
                    try {
                        var msg = System.Text.Json.JsonSerializer.Deserialize<JsMessage>(json);
                        if (msg == null)
                            return;
                        switch (msg.type) {
                            case "_auth":
                                // var uri = new Uri(msg.url.Replace("/fusion/close-app?", $"/fusion/close-app?port={GetRandomUnusedPort()}&", StringComparison.Ordinal));
                                var originalUri = sender.Source;
                                sender.Source = new Uri(msg.url);
                                Debug.WriteLine($"_auth: {msg.url}");
                                var http = new HttpListener();
                                // TODO: use GetRandomUnusedPort()
                                http.Prefixes.Add(loopbackUrl);
                                http.Start();
                                // wait for oauth2 response
                                var context = await http.GetContextAsync().ConfigureAwait(true);
                                var response = context.Response;
                                string responseString = "<html><head></head><body>We are done, please, return to the app.</body></html>";
                                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                                response.ContentLength64 = buffer.Length;
                                var responseOutput = response.OutputStream;
                                await responseOutput.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(true);
                                responseOutput.Close();
                                http.Stop();
                                var secret = context.Request.QueryString.Get("secret")
                                    ?? throw new InvalidOperationException("Secret is null, something went wrong with auth.");
                                Debug.WriteLine($"secret is: {secret}");
                                var cookies = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(Encoding.UTF8.GetString(Convert.FromBase64String(secret)))
                                    ?? throw new InvalidOperationException("Secret in wrong format, something went wrong with auth.");
                                foreach (var (key, value) in cookies) {
                                    var cookie = sender.CoreWebView2.CookieManager.CreateCookie(key, value, "0.0.0.0", "/");
                                    sender.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
                                    cookie = sender.CoreWebView2.CookieManager.CreateCookie(key, value, new Uri(baseUri).Host, "/");
                                    sender.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);

                                    if (OrdinalEquals(key, "FusionAuth.SessionId")) {
                                        string path = Path.Combine(FileSystem.AppDataDirectory, "session.txt");
                                        await File.WriteAllTextAsync(path, value).ConfigureAwait(true);
                                    }

                                }

                                sender.Source = originalUri;
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


            ctrl.CoreWebView2.DOMContentLoaded += async (_, __) => {
                try {
                    await webview.CoreWebView2.ExecuteScriptAsync($"window['_baseURI'] = '{baseUri}'; audio.OpusMediaRecorder.origin='{baseUri}'; ");
                }
                catch (Exception ex) {
                    Debug.WriteLine(ex.ToString());
                }
            };
#pragma warning restore VSTHRD101 // Avoid unsupported async delegates
            ctrl.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            ctrl.CoreWebView2.WebResourceRequested += (CoreWebView2 _, CoreWebView2WebResourceRequestedEventArgs args) => {
                using var deferral = args.GetDeferral();
                /// wss:// can't be rewrited, so we should change SignalR script on the fly
                /// <see href="https://github.com/MicrosoftEdge/WebView2Feedback/issues/172" />
                /// <see href="https://github.com/MicrosoftEdge/WebView2Feedback/issues/685" />
                var uri = args.Request.Uri;
                if (uri.OrdinalStartsWith("https://0.0.0.0/api/")) {
                    args.Request.Uri = uri.OrdinalReplace("https://0.0.0.0/", baseUri);
                    args.Request.Headers.SetHeader("Origin", baseUri);
                    args.Request.Headers.SetHeader("Referer", baseUri);
                    Debug.WriteLine($"webview.WebResourceRequested: Uri is rewritten to {args.Request.Uri}");
                }
                // workaround of 'This browser or app may not be secure.'
                else if (uri.OrdinalStartsWith("https://accounts.google.com")) {
                    args.Request.Headers.SetHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.141 Safari/537.36");
                    args.Request.Headers.SetHeader("sec-ch-ua", "\"Chromium\";v=\"98\", \" Not A;Brand\";v=\"99\"");
                }
                else {
                    Debug.WriteLine($"webview.WebResourceRequested: {uri}");
                }
            };
        }, webview, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.FromCurrentSynchronizationContext());

        return webview;
    }
}
