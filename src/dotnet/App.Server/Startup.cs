using ActualChat.App.Server.Module;
using ActualChat.Audio.Module;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Chat.Module;
using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Contacts.Module;
using ActualChat.Db.Module;
using ActualChat.Feedback.Module;
using ActualChat.Hosting;
using ActualChat.Invite.Module;
using ActualChat.Kubernetes.Module;
using ActualChat.Media.Module;
using ActualChat.MediaPlayback.Module;
using ActualChat.Module;
using ActualChat.Notification.Module;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.Redis.Module;
using ActualChat.Transcription.Module;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Module;
using ActualChat.Users.Module;
using ActualChat.Users.UI.Blazor.Module;
using ActualChat.Web.Module;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging.Console;
using Serilog;

namespace ActualChat.App.Server;

public class Startup
{
    private IConfiguration Cfg { get; }
    private IWebHostEnvironment Env { get; }
    private ModuleHost ModuleHost { get; set; } = null!;

    public Startup(IConfiguration cfg, IWebHostEnvironment environment)
    {
        Cfg = cfg;
        Env = environment;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        var hostSettings = Cfg.GetSettings<HostSettings>();
        var appKind = hostSettings.AppKind ?? AppKind.WebServer;

        // Logging
        services.AddLogging(logging => {
            logging.ClearProviders();
            logging.ConfigureServerFilters(Env.EnvironmentName);
            logging.AddConsole();
#pragma warning disable IL2026
            logging.AddConsoleFormatter<GoogleCloudConsoleFormatter, JsonConsoleFormatterOptions>();
#pragma warning restore IL2026
            if (AppLogging.IsDevLogRequested && appKind.IsServer()) { // This excludes TestServer
                var serilog = new LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .WriteTo.File(AppLogging.DevLogPath,
                        outputTemplate: AppLogging.OutputTemplate,
                        fileSizeLimitBytes: AppLogging.FileSizeLimit)
                    .CreateLogger();
                logging.AddFilteringSerilog(serilog, true);
            }
        });

        // HostInfo
        services.AddSingleton(c => {
            var baseUrl = hostSettings.BaseUri;
            BaseUrlProvider? baseUrlProvider = null;
            if (baseUrl.IsNullOrEmpty()) {
                var server = c.GetRequiredService<IServer>();
                var serverAddressesFeature =
                    server.Features.Get<IServerAddressesFeature>()
                    ?? throw StandardError.NotFound<IServerAddressesFeature>("Can't get server address.");
                baseUrl = serverAddressesFeature.Addresses.FirstOrDefault();
                if (baseUrl.IsNullOrEmpty()) {
                    // If we can't figure out base url at the moment,
                    // lets define a base url provider to resolve base url later on demand.
                    string? resolvedBaseUrl = null;
                    baseUrlProvider = () => {
                        if (resolvedBaseUrl.IsNullOrEmpty()) {
                            resolvedBaseUrl = serverAddressesFeature.Addresses.FirstOrDefault()
                                ?? throw StandardError.NotFound<IServerAddressesFeature>(
                                    "No server addresses found. Most likely you trying to use UrlMapper before the server has started.");
                        }
                        return resolvedBaseUrl;
                    };
                }
            }

            return new HostInfo() {
                AppKind = appKind,
                ClientKind = ClientKind.Unknown,
                Environment = Env.EnvironmentName,
                Configuration = Cfg,
                BaseUrl = baseUrl ?? "",
                BaseUrlProvider = baseUrlProvider,
            };
        });

        var moduleServices = new DefaultServiceProviderFactory().CreateServiceProvider(services);
        ModuleHost = new ModuleHostBuilder()
            // From less dependent to more dependent!
            .WithModules(
                // Core modules
                new CoreModule(moduleServices),
                new KubernetesModule(moduleServices),
                new RedisModule(moduleServices),
                new DbModule(moduleServices),
                new WebModule(moduleServices),
                // Generic modules
                new MediaPlaybackModule(moduleServices),
                // Service-specific & service modules
                new AudioServiceModule(moduleServices),
                new FeedbackServiceModule(moduleServices),
                new MediaServiceModule(moduleServices),
                new ContactsServiceModule(moduleServices),
                new InviteServiceModule(moduleServices),
                new UsersContractsModule(moduleServices),
                new UsersServiceModule(moduleServices),
                new ChatModule(moduleServices),
                new ChatServiceModule(moduleServices),
                new TranscriptionServiceModule(moduleServices),
                new NotificationServiceModule(moduleServices),
                // UI modules
                new BlazorUICoreModule(moduleServices),
                new AudioBlazorUIModule(moduleServices),
                new UsersBlazorUIModule(moduleServices),
                new ChatBlazorUIModule(moduleServices),
                new NotificationBlazorUIModule(moduleServices),
                new BlazorUIAppModule(moduleServices), // Should be the last one in UI section
                // This module should be the last one
                new ServerAppModule(moduleServices)
            ).Build(services);
    }

    public void Configure(IApplicationBuilder app)
    {
        var appHostModule = ModuleHost.GetModule<ServerAppModule>();
        appHostModule.ConfigureApp(app); // This module must be the first one in ConfigureApp call sequence

        ModuleHost.Modules
            .OfType<IWebModule>()
            .Where(m => !ReferenceEquals(m, appHostModule))
            .Apply(m => m.ConfigureApp(app));
    }
}
