using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.ClientApp;

public partial class MauiBlazorWebViewHandler : BlazorWebViewHandler
{
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

    //protected static int GetRandomUnusedPort()
    //{
    //    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    //    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    //    return ((IPEndPoint)socket.LocalEndPoint!).Port;
    //}
}
