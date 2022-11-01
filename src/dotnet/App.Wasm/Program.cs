using ActualChat.Audio.WebM;
using ActualChat.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Wasm;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        var baseUrl = builder.HostEnvironment.BaseAddress;
        await ConfigureServices(builder.Services, builder.Configuration, baseUrl).ConfigureAwait(false);

        var host = builder.Build();
        Constants.HostInfo = host.Services.GetRequiredService<HostInfo>();
        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = host.Services.LogFor(typeof(WebMReader));

        await host.Services.HostedServices().Start().ConfigureAwait(false);
        await host.RunAsync().ConfigureAwait(false);
    }

    public static async Task ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        string baseUrl)
    {
        // Logging
        services.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Debug)
            .AddFilter(null, LogLevel.Information) // Default level
            .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning)
            .AddFilter("Microsoft.AspNetCore.Authorization", LogLevel.Warning)
            .AddFilter("ActualChat", LogLevel.Debug)
            .AddFilter("ActualChat.Audio", LogLevel.Debug)
            .AddFilter("ActualChat.Audio.UI.Blazor", LogLevel.Debug)
            .AddFilter("ActualChat.Audio.UI.Blazor.Components", LogLevel.Debug)
            .AddFilter("ActualChat.Chat", LogLevel.Debug)
            .AddFilter("ActualChat.MediaPlayback", LogLevel.Debug)
            .AddFilter("ActualChat.Audio.Client", LogLevel.Debug)
        );

        // Other services shared with plugins
        services.TryAddSingleton(configuration);
        services.AddSingleton(c => new HostInfo() {
            AppKind = AppKind.Wasm,
            Environment = c.GetService<IWebAssemblyHostEnvironment>()?.Environment ?? "Development",
            Configuration = c.GetRequiredService<IConfiguration>(),
            BaseUrl = baseUrl,
        });

        await AppStartup.ConfigureServices(services).ConfigureAwait(false);
    }
}
