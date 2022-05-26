using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.ClientApp;

public partial class MauiBlazorWebViewHandler : BlazorWebViewHandler
{
    protected readonly ClientAppSettings _settings;
    public MauiBlazorWebViewHandler(ClientAppSettings settings, PropertyMapper? mapper = null) : base(mapper)
    {
        _settings = settings;
    }

    protected static int GetRandomUnusedPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
