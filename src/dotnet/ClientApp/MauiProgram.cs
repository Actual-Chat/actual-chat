using ActualChat.Audio.Client.Module;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Chat.Client.Module;
using ActualChat.Chat.Module;
using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Feedback.Client.Module;
using ActualChat.Hosting;
using ActualChat.MediaPlayback.Module;
using ActualChat.Module;
using ActualChat.UI.Blazor.Module;
using ActualChat.Users.Client.Module;
using ActualChat.Users.UI.Blazor.Module;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.ClientApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .RegisterBlazorMauiWebView()
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });
        // TODO: use resources + new EmbeddedFileProvider(typeof(MauiProgram).Assembly) ?

        var fileprovider = new PhysicalFileProvider(Path.GetDirectoryName(typeof(MauiProgram).Assembly.Location));
        builder.Configuration.AddJsonFile(
            fileprovider,
            "appsettings.Development.json",
            optional: false,
            reloadOnChange: false);
        builder.Configuration.AddJsonFile(
            fileprovider,
            "appsettings.json",
            optional: false,
            reloadOnChange: false);
        var services = builder.Services;
        services.AddBlazorWebView();

        services.TryAddSingleton(builder.Configuration);
        services.AddSingleton(c => new HostInfo() {
            HostKind = HostKind.Blazor,
            RequiredServiceScopes = ImmutableHashSet<Symbol>.Empty
                .Add(ServiceScope.Client)
                .Add(ServiceScope.BlazorUI),
            Environment = c.GetService<IWebAssemblyHostEnvironment>()?.Environment ?? "Development",
            Configuration = c.GetRequiredService<IConfiguration>(),
        });

        builder.ConfigureMauiHandlers(handlers => {
            handlers.AddHandler<IBlazorWebView, MauiBlazorWebViewHandler>();
        });

        var settings = builder.Configuration.Get<ClientAppSettings>();
        if (string.IsNullOrWhiteSpace(settings.BaseUri))
            throw new Exception("Wrong configuration, base uri can't be empty");
        services.TryAddSingleton<ClientAppSettings>(settings);
        services.AddSingleton(_ => new UriMapper(settings.BaseUri));

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
            typeof(FeedbackClientModule)
        );
        // TODO: can CreateMauiApp() be async?
        var plugins = pluginHostBuilder.Build();
        services.AddSingleton(plugins);
        var baseUri = new Uri(settings.BaseUri);
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
        return builder.Build();
    }
}
