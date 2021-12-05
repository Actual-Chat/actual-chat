using ActualChat.Host;
using Microsoft.AspNetCore.Hosting.Server;

namespace ActualChat.Testing.Host;

public static class AppHostExt
{
    public static WebClientTester NewWebClientTester(this AppHost appHost)
        => new(appHost);

    public static PlaywrightTester NewPlaywrightTester(this AppHost appHost)
        => new(appHost);

    public static BlazorTester NewBlazorTester(this AppHost appHost)
        => new(appHost);

    public static HttpClient NewHttpClient(this AppHost appHost)
    {
        var uriMapper = appHost.Services.UriMapper();
        return new() { BaseAddress = uriMapper.BaseUri };
    }

    public static IServer Server(this IServiceProvider services)
        => services.GetRequiredService<IServer>();
}
