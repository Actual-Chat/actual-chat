using ActualChat.App.Server.Logging;
using ActualChat.App.Server.Module;
using ActualChat.Chat.Module;
using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Contacts.Module;
using ActualChat.Contacts.UI.Blazor.Module;
using ActualChat.Db;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Invite.Module;
using ActualChat.Kubernetes.Module;
using ActualChat.Media.Module;
using ActualChat.MLSearch.Module;
using ActualChat.Module;
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
using ActualLab.Diagnostics;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Logging.Console;
using Serilog;
using Serilog.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Server;

public partial class AppHost
{
    public virtual AppHost Build(bool coreServicesOnly = false)
    {
        var ctx = new BuildContext(this) {
            Builder = WebApplication.CreateBuilder(HostOptions),
        };
        var builder = ctx.Builder;
        var env = ctx.Env;
        var cfg = ctx.Cfg;

        /////
        // 1. Configuration
        /////

        // Set default server URL
        cfg.Sources.Insert(0,
            new MemoryConfigurationSource {
                InitialData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
                    { WebHostDefaults.ServerUrlsKey, ServerUrls },
                },
            });
        // Disable FSW, because they eat a lot and can exhaust the handles available to epoll on linux containers
        var jsonProviders = cfg.Sources.OfType<JsonConfigurationSource>().Where(j => j.ReloadOnChange).ToList();
        foreach (var item in jsonProviders) {
            cfg.Sources.Remove(item);
            cfg.AddJsonFile(item.Path!, item.Optional, reloadOnChange: false);
        }
        // Add a few default sources
        cfg.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        cfg.AddEnvironmentVariables();
        ConfigureHost?.Invoke(ctx, cfg);

        /////
        // 2. Module (core) services
        /////

        var hostSettings = cfg.Settings<HostSettings>();
        var appKind = hostSettings.AppKind ?? HostKind.Server;
        var isTested = hostSettings.IsTested ?? false;
        var services = ctx.Services;
        var serverRole = HostRoles.Server.Parse(hostSettings.ServerRole);
        var roles = HostRoles.Server.GetAllRoles(serverRole, isTested);

        // Logging
        services.AddLogging(logging => {
            logging.ClearProviders();
            if (coreServicesOnly)
                return;

            logging.ConfigureServerFilters(env.EnvironmentName);
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

        // HostInfo
        services.AddSingleton(c => {
            var baseUrl = hostSettings.BaseUri;
            if (baseUrl.IsNullOrEmpty()) {
                var baseUrlPrefix = isTested || Equals(env.EnvironmentName, Environments.Development)
                    ? "http" // Any http* endpoint is fine on dev/test
                    : "https://";
                baseUrl = ServerEndpoints.List(c).FirstOrDefault(x => x.OrdinalStartsWith(baseUrlPrefix));
                if (baseUrl.IsNullOrEmpty())
                    throw StandardError.Internal("Can't resolve BaseUrl.");
            }
            return new HostInfo {
                HostKind = appKind,
                AppKind = AppKind.Unknown,
                Environment = env.EnvironmentName,
                Configuration = cfg,
                Roles = roles,
                IsTested = isTested,
                BaseUrl = baseUrl,
            };
        });

        /////
        // 3. ModuleHost & module service
        /////

        ConfigureModuleServices?.Invoke(ctx, services);

        // Notice that we create "partial" service provider here, which contains
        // just the services registered above - i.e. logging + HostInfo
        var moduleServices = ctx.ModuleServices = services.BuildServiceProvider();
        var hostInfo = ctx.HostInfo;
        ctx.Log.IfEnabled(LogLevel.Information)?.LogInformation("HostInfo: {HostInfo}", hostInfo);

        if (coreServicesOnly)
            ctx.ModuleHost = ctx.ModuleHostBuilder.Build(services);
        else {
            ctx.ModuleHost = ctx.ModuleHostBuilder.AddModules(
                // From less dependent to more dependent!
                // Core modules
                new CoreModule(moduleServices),
                new CoreServerModule(moduleServices),
                new KubernetesModule(moduleServices),
                new RedisModule(moduleServices),
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
                new MLSearchServiceModule(moduleServices),
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
            ConfigureServices?.Invoke(ctx, services);
            if (hostInfo.IsDevelopmentInstance)
                ValidateContainerRegistrations(services);
        }
        services.OverrideEntityResolver();

        /////
        // 4. Configure & build WebApplication (IHost)
        /////

        builder.WebHost
            .UseDefaultServiceProvider((_, options) => {
                if (hostInfo.IsDevelopmentInstance) {
                    options.ValidateScopes = true;
                    options.ValidateOnBuild = true;
                }
            })
            .UseKestrel();
        App = ctx.App = builder.Build();
        if (coreServicesOnly)
            return this;

        /////
        // 5. Configure app
        /////

        var moduleHost = ctx.ModuleHost;
        var appServerModule = moduleHost.GetModule<AppServerModule>();
        appServerModule.ConfigureApp(App); // This module must be the first one in ConfigureApp call sequence
        moduleHost.Modules
            .Where(m => m.IsUsed)
            .OfType<IWebServerModule>()
            .Where(m => !ReferenceEquals(m, appServerModule))
            .Apply(m => m.ConfigureApp(App));
        ConfigureApp?.Invoke(ctx, App);
        return this;
    }

    private static void ValidateContainerRegistrations(IServiceCollection services)
    {
        var transientDisposables = services.Where(x => x.Lifetime == ServiceLifetime.Transient)
            .Select(x => AsDisposable(x.ImplementationType))
            .SkipNullItems()
            .Where(x => x.Namespace?.OrdinalIgnoreCaseStartsWith("Microsoft") != true)
            .ToList();
        if (transientDisposables.Count != 0) {
            var transientDisposablesString = string.Join("", transientDisposables.Select(x => $"{Environment.NewLine}- {x}"));
            throw new Exception($"Disposable transient services are not allowed: {transientDisposablesString}");
        }

        Type? AsDisposable(Type? type) => type?.IsAssignableTo(typeof(IDisposable)) == true
            || type?.IsAssignableTo(typeof(IAsyncDisposable)) == true ? type : null;
    }

    // Nested types

    public interface IConfigureHostContext
    {
        AppHost AppHost { get; }
        WebApplicationBuilder Builder { get; }
        IWebHostEnvironment Env { get; }
        IConfigurationManager Cfg { get; }
    }

    public interface IConfigureModuleServicesContext : IConfigureHostContext
    {
        IServiceCollection Services { get; }
        ModuleHostBuilder ModuleHostBuilder { get; }
    }

    public interface IConfigureServicesContext : IConfigureModuleServicesContext
    {
        IServiceProvider ModuleServices { get; }
        ModuleHost ModuleHost { get; }
        HostInfo HostInfo { get; }
        ILogger Log { get; }
    }

    public interface IConfigureAppContext : IConfigureServicesContext
    {
        WebApplication App { get; }
    }

    public sealed class BuildContext(AppHost appHost) : IConfigureAppContext
    {
        private HostInfo? _hostInfo;
        private ILogger<BuildContext>? _log;

        public AppHost AppHost { get; set; } = appHost;
        public WebApplicationBuilder Builder { get; set; } = null!;
        public IWebHostEnvironment Env => Builder.Environment;
        public IConfigurationManager Cfg => Builder.Configuration;
        public IServiceCollection Services => Builder.Services;
        public IServiceProvider ModuleServices { get; set; } = null!;
        public ModuleHostBuilder ModuleHostBuilder { get; set; } = new();
        public ModuleHost ModuleHost { get; set; } = null!;
        public HostInfo HostInfo => _hostInfo ??= ModuleServices.GetRequiredService<HostInfo>();
        public ILogger Log => _log ??= ModuleServices.LogFor<BuildContext>();
        public WebApplication App { get; set; } = null!;
    }
}
