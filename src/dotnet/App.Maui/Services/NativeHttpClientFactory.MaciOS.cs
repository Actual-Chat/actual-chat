using System.Security.Authentication;

namespace ActualChat.App.Maui.Services;

public partial class NativeHttpClientFactory
{
    private partial HttpMessageHandler? CreatePlatformMessageHandler()
        => new NSUrlSessionHandler {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            UseCookies = true,
            MaxConnectionsPerServer = 200,
        };
}
