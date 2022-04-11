using ActualChat.Audio.Client.Module;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Audio.WebM;
using ActualChat.Chat.Client.Module;
using ActualChat.Chat.Module;
using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Feedback.Client.Module;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.MediaPlayback.Module;
using ActualChat.Notification.Client.Module;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.UI.Blazor.Module;
using ActualChat.Users.Client.Module;
using ActualChat.Users.UI.Blazor.Module;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        var baseUri = new Uri(builder.HostEnvironment.BaseAddress);
        await ConfigureServices(builder.Services, builder.Configuration, baseUri).ConfigureAwait(false);

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
        Uri baseUri)
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
            HostKind = HostKind.Blazor,
            RequiredServiceScopes = ImmutableHashSet<Symbol>.Empty
                .Add(ServiceScope.Client)
                .Add(ServiceScope.BlazorUI),
            Environment = c.GetService<IWebAssemblyHostEnvironment>()?.Environment ?? "Development",
            Configuration = c.GetRequiredService<IConfiguration>(),
        });
        services.AddSingleton(_ => new UriMapper(baseUri));

        // Creating plugins
        var pluginHostBuilder = new PluginHostBuilder(new ServiceCollection().Add(services));
        // FileSystemPluginFinder doesn't work in Blazor, so we have to enumerate them explicitly
        pluginHostBuilder.UsePlugins(
            typeof(CoreModule),
            typeof(PlaybackModule),
            typeof(BlazorUICoreModule),
            typeof(AudioClientModule),
            typeof(AudioBlazorUIModule),
            typeof(ChatModule),
            typeof(ChatClientModule),
            typeof(ChatBlazorUIModule),
            typeof(UsersClientModule),
            typeof(UsersBlazorUIModule),
            typeof(FeedbackClientModule),
            typeof(NotificationClientModule),
            typeof(NotificationBlazorUIModule)
        );
        var plugins = await pluginHostBuilder.BuildAsync().ConfigureAwait(false);
        services.AddSingleton(plugins);

        // Fusion services
        var fusion = services.AddFusion();
        var fusionClient = fusion.AddRestEaseClient((_, o) => {
            o.BaseUri = baseUri;
            o.IsLoggingEnabled = true;
            o.IsMessageLoggingEnabled = false;
        });
        fusionClient.ConfigureHttpClientFactory((c, name, o) => {
            var uriMapper = c.GetRequiredService<UriMapper>();
            var apiBaseUri = uriMapper.ToAbsolute("api/");
            var isFusionClient = (name ?? "").StartsWith("Stl.Fusion", StringComparison.Ordinal);
            var clientBaseUri = isFusionClient ? baseUri : apiBaseUri;
            o.HttpClientActions.Add(client => client.BaseAddress = clientBaseUri);
        });

        // Injecting plugin services
        plugins.GetPlugins<HostModule>().Apply(m => m.InjectServices(services));
    }
}
