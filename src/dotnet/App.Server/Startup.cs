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
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Module;
using ActualChat.Users.Module;
using ActualChat.Users.UI.Blazor.Module;
using ActualChat.Web.Module;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging.Console;

namespace ActualChat.App.Server;

public class Startup
{
    private IConfiguration Cfg { get; }
    private IWebHostEnvironment Env { get; }
    private ImmutableArray<HostModule> HostModules { get; set; } = ImmutableArray<HostModule>.Empty;

    public Startup(IConfiguration cfg, IWebHostEnvironment environment)
    {
        Cfg = cfg;
        Env = environment;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<TracerProvider>(_ => new CircuitTracerProvider());

        // Logging
        services.AddLogging(logging => {
            logging.ClearProviders();
            logging.AddConsole();
            var devLogPath = Environment.GetEnvironmentVariable("DevLog");
            if (!devLogPath.IsNullOrEmpty())
                logging.AddFile(
                    devLogPath,
                    LogLevel.Debug,
                    new Dictionary<string, LogLevel>(StringComparer.Ordinal) {
                        { "ActualChat", LogLevel.Debug },
                        { "ActualChat.Transcription", LogLevel.Debug },
                        { "ActualChat.Transcription.Google", LogLevel.Debug },
                        { "Microsoft", LogLevel.Warning },
                        // { "Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Debug },
                        // { "Microsoft.AspNetCore.Components", LogLevel.Debug },
                        { "Stl", LogLevel.Warning },
                        { "Stl.Fusion", LogLevel.Information },
                    },
                    retainedFileCountLimit: 1,
                    outputTemplate: "{Timestamp:mm:ss.fff} {Level:u3}-{SourceContext} {Message}{NewLine}{Exception}"
                    );

#pragma warning disable IL2026
            logging.AddConsoleFormatter<GoogleCloudConsoleFormatter, JsonConsoleFormatterOptions>();
#pragma warning restore IL2026
            logging.SetMinimumLevel(Env.IsDevelopment() ? LogLevel.Debug : LogLevel.Warning);
            // Use appsettings*.json to configure logging filters
        });

        // HostInfo
        services.AddSingleton(c => {
            var hostSettings = Cfg.GetSettings<HostSettings>();
            var baseUrl = hostSettings.BaseUrl;
            Func<string>? baseUrlProvider = null;
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
                AppKind = hostSettings.AppKind ?? AppKind.WebServer,
                Environment = Env.EnvironmentName,
                Configuration = Cfg,
                BaseUrl = baseUrl ?? "",
                BaseUrlProvider = baseUrlProvider,
            };
        });

        // Commander - it must be added first to make sure its options are set
        var commander = services.AddCommander().Configure(new CommanderOptions {
            AllowDirectCommandHandlerCalls = false,
        });

        var serviceProvider = new DefaultServiceProviderFactory().CreateServiceProvider(services);
        var host = new ModuleHostBuilder()
            .AddModule(
                new CoreModule(serviceProvider),
                new AppHostModule(serviceProvider),
                new BlazorUIAppModule(serviceProvider),
                new BlazorUICoreModule(serviceProvider),
                new AudioBlazorUIModule(serviceProvider),
                new ChatBlazorUIModule(serviceProvider),
                new NotificationBlazorUIModule(serviceProvider),
                new UsersBlazorUIModule(serviceProvider),
                new ChatModule(serviceProvider),
                new TranscriptionModule(serviceProvider),
                new UsersContractsModule(serviceProvider),
                new PlaybackModule(serviceProvider),
                new WebModule(serviceProvider),
                new AudioModule(serviceProvider),
                new ChatServiceModule(serviceProvider),
                new ContactsServiceModule(serviceProvider),
                new FeedbackModule(serviceProvider),
                new InviteServiceModule(serviceProvider),
                new MediaServiceModule(serviceProvider),
                new NotificationModule(serviceProvider),
                new UsersServiceModule(serviceProvider),
                new KubernetesModule(serviceProvider),
                new RedisModule(serviceProvider),
                new DbModule(serviceProvider))
            .Build(services);
        HostModules = host.GetModules();
    }

    public void Configure(IApplicationBuilder app)
        => HostModules.OfType<IWebModule>().Apply(m => m.ConfigureApp(app));
}
