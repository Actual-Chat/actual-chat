using System.Security.Authentication;
using Xamarin.Android.Net;

namespace ActualChat.App.Maui.Services;

public partial class NativeHttpClientFactory
{
    private partial HttpMessageHandler? CreatePlatformMessageHandler()
        => new AndroidMessageHandler {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            UseCookies = true,
            MaxConnectionsPerServer = 200,
        };
}
