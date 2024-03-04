using ActualChat.App.Server.Logging;
using ActualChat.App.Server.Module;
using ActualChat.Chat.Module;
using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Commands;
using ActualChat.Contacts.Module;
using ActualChat.Contacts.UI.Blazor.Module;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Invite.Module;
using ActualChat.Kubernetes.Module;
using ActualChat.Media.Module;
using ActualChat.Module;
using ActualChat.Nats.Module;
using ActualChat.Notification.Module;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.Redis.Module;
using ActualChat.Search.Module;
using ActualChat.Streaming.Module;
using ActualChat.Streaming.UI.Blazor.Module;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Module;
using ActualChat.Users.Module;
using ActualChat.Users.UI.Blazor.Module;
using Microsoft.Extensions.Logging.Console;
using Serilog;
using Serilog.Events;

namespace ActualChat.App.Server;

public class Startup(IConfiguration cfg, IWebHostEnvironment environment)
{
    private IConfiguration Cfg { get; } = cfg;
    private IWebHostEnvironment Env { get; } = environment;
    private ModuleHost ModuleHost { get; set; } = null!;

    public void ConfigureServices(IServiceCollection services)
    {
        var hostSettings = Cfg.GetSettings<HostSettings>();
        var appKind = hostSettings.AppKind ?? HostKind.Server;
        var isTested = hostSettings.IsTested ?? false;

        // Logging
        services.AddLogging(logging => {
            logging.ClearProviders();
            logging.ConfigureServerFilters(Env.EnvironmentName);
            logging.AddConsole();
            logging.AddConsoleFormatter<GoogleCloudConsoleFormatter, JsonConsoleFormatterOptions>();
            if (!AppLogging.DevLog.IsEmpty && appKind.IsServer() && !isTested) {
                var serilog = new LoggerConfiguration()
                    .MinimumLevel.Is(LogEventLevel.Verbose)
                    .Enrich.FromLogContext()
                    .Enrich.With(new ProcessIdLogEventEnricher())
                    .Enrich.With(new ThreadIdLogEventEnricher())
                    .WriteTo.File(AppLogging.DevLog,
                        outputTemplate: AppLogging.DevLogOutputTemplate,
                        fileSizeLimitBytes: AppLogging.DevLogFileSizeLimit,
                        shared: true)
                    .CreateLogger();
                logging.AddFilteringSerilog(serilog, true);
            }
        });

        var serverRole = HostRoles.Server.Parse(hostSettings.ServerRole);
        var roles = HostRoles.Server.GetAllRoles(serverRole);
        var commandQueueRoles = hostSettings.CommandQueueRoles
            .Split(',')
            .Select(sr => HostRoles.Queue.Parse(sr.Trim()))
            .Where(hr => hr.IsQueue)
            .ToHashSet();
        var combinedRoles = roles
            .Concat(commandQueueRoles)
            .ToImmutableHashSet();

        // HostInfo
        services.AddSingleton(c => {
            var baseUrl = hostSettings.BaseUri;
            if (baseUrl.IsNullOrEmpty()) {
                var baseUrlPrefix = isTested || Equals(Env.EnvironmentName, Environments.Development)
                    ? "http" // Any http* endpoint is fine on dev/test
                    : "https://";
                baseUrl = ServerEndpoints.List(c).FirstOrDefault(x => x.OrdinalStartsWith(baseUrlPrefix));
                if (baseUrl.IsNullOrEmpty())
                    throw StandardError.Internal("Can't resolve BaseUrl.");
            }
            return new HostInfo {
                HostKind = appKind,
                AppKind = AppKind.Unknown,
                Environment = Env.EnvironmentName,
                Configuration = Cfg,
                Roles = combinedRoles,
                IsTested = isTested,
                BaseUrl = baseUrl,
            };
        });

        // Configure lifecycle monitor
        services.AddHostedService<AppHostLifecycleMonitor>();

        var moduleServices = new DefaultServiceProviderFactory().CreateServiceProvider(services);
        ModuleHost = new ModuleHostBuilder()
            // From less dependent to more dependent!
            .WithModules(
                // Core modules
                new CoreModule(moduleServices),
                new CoreServerModule(moduleServices),
                new KubernetesModule(moduleServices),
                new RedisModule(moduleServices),
                new NatsModule(moduleServices),
                new DbModule(moduleServices),
                // API modules
                new ApiModule(moduleServices),
                // Service-specific & service modules
                new StreamingServiceModule(moduleServices),
                new MediaServiceModule(moduleServices),
                new ContactsServiceModule(moduleServices),
                new InviteServiceModule(moduleServices),
                new UsersServiceModule(moduleServices),
                new ChatServiceModule(moduleServices),
                new NotificationServiceModule(moduleServices),
                new SearchServiceModule(moduleServices),
                // UI modules
                new BlazorUICoreModule(moduleServices),
                new StreamingBlazorUIModule(moduleServices),
                new UsersBlazorUIModule(moduleServices),
                new ContactsBlazorUIModule(moduleServices),
                new ChatBlazorUIModule(moduleServices),
                new NotificationBlazorUIModule(moduleServices),
                new BlazorUIAppModule(moduleServices), // Should be the last one in UI section
                // This module should be the last one
                new AppServerModule(moduleServices)
            ).Build(services);

        // Default configuration for command/event queues
        services.AddCommandQueues(roles);
    }

    public void Configure(IApplicationBuilder app)
    {
        var appHostModule = ModuleHost.GetModule<AppServerModule>();
        appHostModule.ConfigureApp(app); // This module must be the first one in ConfigureApp call sequence

        ModuleHost.Modules
            .OfType<IWebServerModule>()
            .Where(m => !ReferenceEquals(m, appHostModule))
            .Apply(m => m.ConfigureApp(app));
    }
}
