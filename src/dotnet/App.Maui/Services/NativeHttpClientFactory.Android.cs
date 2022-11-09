using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using Xamarin.Android.Net;

namespace ActualChat.App.Maui.Services;

public partial class NativeHttpClientFactory
{
    private partial HttpMessageHandler? CreatePlatformMessageHandler()
        => new AndroidMessageHandler {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
            MaxConnectionsPerServer = 200,
        };


    // this code can be used for debugging purposes
    // private class NativeMessageHandler : AndroidMessageHandler
    // {
    //     protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    //     {
    //         try {
    //             var response = await base.SendAsync(request, cancellationToken);
    //             var content = await response.Content.ReadAsStringAsync();
    //             Console.WriteLine(content);
    //             return response;
    //         }
    //         catch (Exception e) {
    //             Console.WriteLine(e.ToString());
    //             throw;
    //         }
    //     }
    // }
}
