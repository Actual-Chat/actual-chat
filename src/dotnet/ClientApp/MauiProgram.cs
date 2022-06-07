using ActualChat.Audio.Client.Module;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Chat.Client.Module;
using ActualChat.Chat.Module;
using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Feedback.Client.Module;
using ActualChat.Hosting;
using ActualChat.MediaPlayback.Module;
using ActualChat.Module;
using ActualChat.Notification.UI.Blazor.Module;
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
//using Serilog;

namespace ActualChat.ClientApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });
        // TODO: use resources + new EmbeddedFileProvider(typeof(MauiProgram).Assembly) ?

        var fileprovider = new EmbeddedFileProvider(typeof(MauiProgram).Assembly);
        var files = fileprovider.GetDirectoryContents("").ToArray();
        builder.Configuration.AddJsonFile(
            fileprovider,
            "appsettings.Development.json",
            // TODO: fix android fs access
            optional: true,
            reloadOnChange: false);
        builder.Configuration.AddJsonFile(
            fileprovider,
            // TODO: fix android fs access
            "appsettings.json",
            optional: true,
            reloadOnChange: false);
        var services = builder.Services;
        services.AddMauiBlazorWebView();

#if DEBUG || DEBUG_MAUI
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

  //      var appName = typeof(MauiProgram).Assembly.GetName().Name;
  //      Log.Logger = new LoggerConfiguration()
  //          .MinimumLevel.Debug()
  //          .WriteTo.File($"C:\\Logs\\maui.{appName}.log")
  //          .CreateLogger();

		//builder.Logging.AddSerilog();

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

        //var settings = builder.Configuration.Get<ClientAppSettings>();
        // host address for local debugging
        // https://devblogs.microsoft.com/xamarin/debug-local-asp-net-core-web-apis-android-emulators/
        // https://developer.android.com/studio/run/emulator-networking.html
        // Unfortunately, this base address does not work WSA.
        // TODO(DF): find solution for WSA
        var ipAddress = DeviceInfo.Platform == DevicePlatform.Android ? "10.0.2.2" : "localhost";
        var backendUrl = $"http://{ipAddress}:7080";
        // To use BaseUri : https://local.actual.chat
        // We need to modify hosts file on android emulator similar how we did it for windows hosts.
        // Using instrunctions from https://csimpi.medium.com/android-emulator-add-hosts-file-f4c73447453e,
        // add line to the hosts file:
        // 10.0.2.2		local.actual.chat
        // Emulator has to be started with -writable-system flag every time to see hosts changes
        // See comments to https://stackoverflow.com/questions/41117715/how-to-edit-etc-hosts-file-in-android-studio-emulator-running-in-nougat/47622017#47622017
        var settings = new ClientAppSettings {
            //BaseUri = "https://localhost:7081"
            //BaseUri = "https://dev.actual.chat"
            //BaseUri = backendUrl,
            BaseUri = "https://local.actual.chat"
        };
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
            typeof(FeedbackClientModule),
            typeof(NotificationBlazorUIModule)
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
            o.IsMessageLoggingEnabled = true;
        });
        fusionClient.ConfigureHttpClientFactory((c, name, o) => {
            var uriMapper = c.GetRequiredService<UriMapper>();
            var apiBaseUri = uriMapper.ToAbsolute("api/");
            var isFusionClient = (name ?? "").OrdinalStartsWith("Stl.Fusion");
            var clientBaseUri = isFusionClient ? baseUri : apiBaseUri;
            o.HttpClientActions.Add(client => client.BaseAddress = clientBaseUri);
        });

        // Injecting plugin services
        plugins.GetPlugins<HostModule>().Apply(m => m.InjectServices(services));

        //Log.Logger.Information("test. starting.");

        return builder.Build();
    }
}
