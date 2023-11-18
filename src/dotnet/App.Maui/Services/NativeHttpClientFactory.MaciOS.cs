using System.Security.Authentication;

namespace ActualChat.App.Maui.Services;

#pragma warning disable CA5398 // Avoid hardcoding SslProtocols 'Tls12' to ensure your application remains secure in the future.
#pragma warning disable CA1822 // Member 'CreatePlatformMessageHandler' does not access instance data and can be marked as static

public partial class NativeHttpClientFactory
{
    private partial HttpMessageHandler? CreatePlatformMessageHandler()
        => new NSUrlSessionHandler {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            UseCookies = false,
            MaxConnectionsPerServer = 200,
        };
}
