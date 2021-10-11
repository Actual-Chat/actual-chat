using ActualChat.Audio.Client.Module;
using ActualChat.Audio.UI.Blazor;
using ActualChat.Chat.Client.Module;
using ActualChat.Chat.UI.Blazor;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Users.Client.Module;
using ActualChat.Users.UI.Blazor;
using ActualChat.Users.UI.Blazor.Module;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.DependencyInjection;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        await ConfigureServices(builder.Services, builder);
        var host = builder.Build();
        await host.Services.HostedServices().Start();
        await host.RunAsync();
    }

    public static async Task ConfigureServices(IServiceCollection services, WebAssemblyHostBuilder builder)
    {
        // Logging
        var logging = builder.Logging;
        logging.SetMinimumLevel(LogLevel.Information);

        // Other services shared with plugins
        services.AddSingleton(new HostInfo() {
            HostKind = HostKind.Blazor,
            RequiredServiceScopes = ImmutableHashSet<Symbol>.Empty
                .Add(ServiceScope.Client)
                .Add(ServiceScope.BlazorUI),
            Environment = builder.HostEnvironment.Environment,
            Configuration = builder.Configuration,
        });

        // Creating plugins
        var pluginHostBuilder = new PluginHostBuilder(new ServiceCollection().Add(services));
        // FileSystemPluginFinder doesn't work in Blazor, so we have to enumerate them explicitly
        pluginHostBuilder.UsePlugins(
            typeof(CoreModule),
            typeof(BlazorUICoreModule),
            typeof(AudioClientModule),
            typeof(AudioBlazorUIModule),
            typeof(ChatClientModule),
            typeof(ChatBlazorUIModule),
            typeof(UsersClientModule),
            typeof(UsersBlazorUIModule)
        );
        var plugins = await pluginHostBuilder.BuildAsync();
        services.AddSingleton(plugins);

        var baseUri = new Uri(builder.HostEnvironment.BaseAddress);
        var apiBaseUri = new Uri($"{baseUri}api/");

        // Fusion services
        var fusion = services.AddFusion();
        var fusionClient = fusion.AddRestEaseClient((_, o) => {
            o.BaseUri = baseUri;
            o.IsLoggingEnabled = true;
            o.IsMessageLoggingEnabled = false;
        });
        fusionClient.ConfigureHttpClientFactory((c, name, o) => {
            var isFusionClient = (name ?? "").StartsWith("Stl.Fusion", StringComparison.Ordinal);
            var clientBaseUri = isFusionClient ? baseUri : apiBaseUri;
            o.HttpClientActions.Add(client => client.BaseAddress = clientBaseUri);
        });

        // Injecting plugin services
        plugins.GetPlugins<HostModule>().Apply(m => m.InjectServices(services));

        // UriMapper
        services.AddSingleton(c => new UriMapper(baseUri));
    }
}
