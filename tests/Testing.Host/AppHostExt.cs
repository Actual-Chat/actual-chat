using ActualChat.App.Server;
using Microsoft.AspNetCore.Hosting.Server;

namespace ActualChat.Testing.Host;

public static class AppHostExt
{
    public static WebClientTester NewWebClientTester(
        this AppHost appHost,
        ITestOutputHelper output,
        Action<IServiceCollection>? configureClientServices = null)
        => new(appHost, output, configureClientServices);

    public static PlaywrightTester NewPlaywrightTester(this AppHost appHost, ITestOutputHelper @out)
        => new(appHost, @out);

    public static BlazorTester NewBlazorTester(this AppHost appHost, ITestOutputHelper @out)
        => new(appHost, @out);

    public static HttpClient NewHttpClient(this AppHost appHost)
    {
        var urlMapper = appHost.Services.UrlMapper();
        return new() { BaseAddress = urlMapper.BaseUri };
    }

    public static IServer Server(this IServiceProvider services)
        => services.GetRequiredService<IServer>();
}
