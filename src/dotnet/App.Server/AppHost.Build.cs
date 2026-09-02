using ActualChat.App.Server.Logging;
using ActualChat.App.Server.Module;
using ActualChat.Chat.Module;
using ActualChat.Contacts.Module;
using ActualChat.Db.Module;
using ActualChat.Flows.Module;
using ActualChat.Invite.Module;
using ActualChat.Kubernetes.Module;
using ActualChat.Logging;
using ActualChat.Mcp.Module;
using ActualChat.Media.Module;
using ActualChat.MLSearch.Module;
using ActualChat.Module;
using ActualChat.Notifications.Module;
using ActualChat.Redis.Module;
using ActualChat.Streaming.Module;
using ActualChat.Transcription.Module;
using ActualChat.UI;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Module;
using ActualChat.UI.Module;
using ActualChat.Users.Module;
using ActualLab.Diagnostics;
using DotNetEnv.Configuration;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Serilog;
using Serilog.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Tracer = ActualChat.Performance.Tracer;

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
        cfg.AddDotNetEnv(Path.Combine(AppContext.BaseDirectory, ".env"), new(setEnvVars: false));
        cfg.AddEnvironmentVariables("ASPNETCORE_");
        cfg.AddEnvironmentVariables();
        // If HostSettings:BasePort is set (e.g. from .env for worktrees), use it as server URL.
        // In Aspire mode, ASPNETCORE_URLS env var (line above) takes precedence.
        if (int.TryParse(cfg["HostSettings:BasePort"], out var basePort))
            cfg.AddInMemoryCollection([new(WebHostDefaults.ServerUrlsKey, $"http://0.0.0.0:{basePort}")]);
        ConfigureHost?.Invoke(ctx, cfg);

        /////
        // 2. Module (core) services
        /////

        var hostSettings = cfg.Settings<HostSettings>();
        var appKind = hostSettings.AppKind ?? HostKind.Server;
        var isTested = hostSettings.IsTested ?? false;
        var isLocalDev = Constants.Hosts.IsLocalDev(new Uri(hostSettings.BaseUri).Host);
        var services = ctx.Services;
        var serverRole = HostRoles.Server.Parse(hostSettings.ServerRole);
        var roles = HostRoles.Server.GetAllRoles(serverRole, isTested);

        // Tracers
        services.AddTracers(Tracer.Default, useScopedTracers: true);

        // Logging
        Console.WriteLine("About to configure logging for env: '{0}'", env.EnvironmentName);
        services.AddLogging(logging => {
            logging.ClearProviders();
            if (coreServicesOnly || isTested)
                return;

            logging.ConfigureServerFilters(env.EnvironmentName);
            if (isLocalDev)
                logging.AddConsole();
            logging.AddTailLogger();
            if (!appKind.IsServer() || isTested)
                return;
#if DEBUG
            if (isLocalDev)
                logging.AddSeq();
#endif

            if (!LoggingExt.DevLog.IsEmpty) {
                var serilog = new LoggerConfiguration()
                    .MinimumLevel.Is(LogEventLevel.Verbose)
                    .Enrich.FromLogContext()
                    .Enrich.With(new ProcessIdLogEventEnricher())
                    .Enrich.With(new ThreadIdLogEventEnricher())
                    .WriteTo.File(LoggingExt.DevLog,
                        outputTemplate: LoggingExt.OutputTemplate,
                        fileSizeLimitBytes: LoggingExt.DevLogFileSizeLimit,
                        shared: true)
                    .CreateLogger();
                logging.AddFilteringSerilog(serilog, true);
            }
            logging.AddSanitizingLoggerFactory(c => c.HostInfo().IsProductionInstance);
        });

        // HostInfo
        services.AddSingleton(c => {
            var baseUrl = hostSettings.BaseUri;
            if (baseUrl.IsNullOrEmpty()) {
                var baseUrlPrefix = isTested || Equals(env.EnvironmentName, Environments.Development)
                    ? "http" // Any http* endpoint is fine on dev/test
                    : "https://";
                baseUrl = ServerEndpoints.List(c).FirstOrDefault(x => x.StartsWith(baseUrlPrefix));
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

        // ApplicationPartManager - if the instance of this service is available,
        // AddMvcCore uses it instead of populating application parts on each call.
        // We populate application parts manually - see how applicationPartManager
        // is used below.
        var applicationPartManager = new ApplicationPartManager();
        services.AddSingleton(applicationPartManager);

        /////
        // 3. ModuleHost & module service
        /////

        ConfigureModuleServices?.Invoke(ctx, services);

        // Notice that we create a "partial" service provider here, which contains
        // just the services registered above - i.e. logging + HostInfo
        var moduleServices = ctx.ModuleServices = services.BuildServiceProvider();
        var hostInfo = ctx.HostInfo;
        ctx.Log.IfEnabled(LogLevel.Information)?.LogInformation("HostInfo: {HostInfo}", hostInfo);

        var moduleHostBuilder = ctx.ModuleHostBuilder;
        if (coreServicesOnly)
            ctx.ModuleHost = moduleHostBuilder.Build(services);
        else {
            moduleHostBuilder.AddModules(
                // From less dependent to more dependent!
                // Core modules
                new CoreModule(moduleServices),
                new CoreServerModule(moduleServices),
                new KubernetesModule(moduleServices),
                new RedisModule(moduleServices),
                new DbModule(moduleServices),
                new FlowsServiceModule(moduleServices),
                // API modules
                new ApiModule(moduleServices),
                // Service-specific & service modules
                new TranscriptionServiceModule(moduleServices),
                new StreamingServiceModule(moduleServices),
                new MediaServiceModule(moduleServices),
                new ContactsServiceModule(moduleServices),
                new InviteServiceModule(moduleServices),
                new UsersServiceModule(moduleServices),
                new ChatServiceModule(moduleServices),
                new NotificationServiceModule(moduleServices),
                new MLSearchServiceModule(moduleServices),
                new McpModule(moduleServices),
                // UI modules
                new UICoreModule(moduleServices),
                new BlazorUICoreModule(moduleServices),
                new BlazorUIAppModule(moduleServices), // Should be the last one in UI section
                // This module should be the last one
                new AppServerModule(moduleServices)
            );
            // One AssemblyPart per each module's assembly
            foreach (var assembly in moduleHostBuilder.Modules.Select(m => m.GetType().Assembly).Distinct())
                applicationPartManager.ApplicationParts.Add(new AssemblyPart(assembly));
            ctx.ModuleHost = moduleHostBuilder.Build(services);
            ConfigureServices?.Invoke(ctx, services);
            if (hostInfo.IsDevelopmentInstance)
                ValidateContainerRegistrations(services);
        }

        /////
        // 4. Configure & build WebApplication (IHost)
        /////

        builder.Host.ConfigureHostOptions(options => {
            options.ServicesStartConcurrently = true;
            options.ServicesStopConcurrently = true;
        });
        builder.WebHost
            .UseDefaultServiceProvider((_, options) => {
                if (hostInfo.IsDevelopmentInstance) {
                    options.ValidateScopes = true;
                    options.ValidateOnBuild = true;
                }
            })
            .UseKestrel(options => {
                // GFE preserves the original client :scheme (https) when proxying
                // over h2c to /rpc/http. Without this, Kestrel rejects :scheme=https
                // on a plaintext listener with PROTOCOL_ERROR.
                options.AllowAlternateSchemes = true;
            });
        App = ctx.App = builder.Build();
        if (coreServicesOnly)
            return this;

        /////
        // 5. Configure app
        /////

        // Add COOP/COEP headers for main page early in the pipeline
        // App.UseCoopHeaders();

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
            .Where(x => x.Namespace?.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) != true)
            .ToList();
        if (transientDisposables.Count != 0) {
            var transientDisposablesString = string.Join("", transientDisposables.Select(x => $"{Environment.NewLine}- {x}"));
            throw StandardError.Internal($"Disposable transient services are not allowed: {transientDisposablesString}");
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
        public AppHost AppHost { get; set; } = appHost;
        public WebApplicationBuilder Builder { get; set; } = null!;
        public IWebHostEnvironment Env => Builder.Environment;
        public IConfigurationManager Cfg => Builder.Configuration;
        public IServiceCollection Services => Builder.Services;
        public IServiceProvider ModuleServices { get; set; } = null!;
        public ModuleHostBuilder ModuleHostBuilder { get; set; } = new();
        public ModuleHost ModuleHost { get; set; } = null!;
        public HostInfo HostInfo => field ??= ModuleServices.GetRequiredService<HostInfo>();
        public ILogger Log => field ??= ModuleServices.LogFor<BuildContext>();
        public WebApplication App { get; set; } = null!;
    }
}
