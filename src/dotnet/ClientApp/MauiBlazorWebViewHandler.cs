using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.ClientApp;

public partial class MauiBlazorWebViewHandler : BlazorWebViewHandler
{
    private const string loopbackUrl = "http://127.0.0.1:57348/";
    private record JsMessage(string type, string url);

    private string? _baseUri;

    public string BaseUri {
        get {
            if (_baseUri == null) { 
                var settings = MauiContext!.Services.GetRequiredService<ClientAppSettings>();
                _baseUri = settings.BaseUri!;
                if (!_baseUri.EndsWith('/'))
                    _baseUri += '/';
            }
            return _baseUri;
        }
    }

    public MauiBlazorWebViewHandler()
    {
        // Intentionally use parameterless constructor.
        // Consturctor with parameters causes Exception on Android platform:
        // Microsoft.Maui.Platform.ToPlatformException
        // Message = Microsoft.Maui.Handlers.PageHandler found for ActualChat.ClientApp.MainPage is incompatible
    }

    private static async Task<KeyValuePair<string, string>[]> GetRedirectSecret()
    {
        var http = new HttpListener();
        // TODO: use GetRandomUnusedPort()
        http.Prefixes.Add(loopbackUrl);
        http.Start();
        // wait for oauth2 response
        var context = await http.GetContextAsync().ConfigureAwait(true);
        var response = context.Response;
        string responseString = "<html><head></head><body>We are done, please, return to the app.<script>setTimeout(function() { window.close(); }, 500)</script></body></html>";
        byte[] buffer = Encoding.UTF8.GetBytes(responseString);
        response.ContentLength64 = buffer.Length;
        var responseOutput = response.OutputStream;
        await responseOutput.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(true);
        responseOutput.Close();
        http.Stop();
        var secret = context.Request.QueryString.Get("secret")
            ?? throw new InvalidOperationException("Secret is null, something went wrong with auth.");
        Debug.WriteLine($"secret is: {secret}");
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(secret));
        var cookies = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(json)
                                ?? throw new InvalidOperationException("Secret in wrong format, something went wrong with auth.");
        return cookies;
    }

    private static async Task<bool> OpenSystemBrowserForSignIn(string url)
    {
        //var uri = new Uri(msg.url.Replace("/fusion/close-app?", $"/fusion/close-app?port={GetRandomUnusedPort()}&", StringComparison.Ordinal));
        Debug.WriteLine($"_auth: {url}");
        try {
            var uri = new Uri(url);
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex) {
            // An unexpected error occured. No browser may be installed on the device.
            Debug.WriteLine("_auth: failed to open browser. Exception: " + ex);
            return false;
        }
    }

    //protected static int GetRandomUnusedPort()
    //{
    //    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    //    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    //    return ((IPEndPoint)socket.LocalEndPoint!).Port;
    //}
}
