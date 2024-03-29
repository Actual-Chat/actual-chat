using ActualChat.App.Server.Logging;
using ActualChat.App.Server.Module;
using ActualChat.Chat.Module;
using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Contacts.Module;
using ActualChat.Contacts.UI.Blazor.Module;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Invite.Module;
using ActualChat.Kubernetes.Module;
using ActualChat.Media.Module;
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

public sealed class AppHostBuilder
{
    private HostInfo? _hostInfo;
    private ILogger<AppHostBuilder>? _log;

    public AppHost AppHost { get; }
    public WebApplicationBuilder Builder { get; }
    public IWebHostEnvironment Env => Builder.Environment;
    public IConfigurationManager Cfg => Builder.Configuration;
    public IServiceCollection Services => Builder.Services;
    public IServiceProvider ModuleServices { get; }
    public ModuleHost ModuleHost { get; }
    public HostInfo HostInfo => _hostInfo ??= ModuleServices.GetRequiredService<HostInfo>();
    public ILogger Log => _log ??= ModuleServices.LogFor<AppHostBuilder>();
    public WebApplication App { get; }

    public AppHostBuilder(AppHost appHost, bool configurationOnly = false)
    {
        AppHost = appHost;
        Builder = WebApplication.CreateBuilder(AppHost.HostOptions);

        /////
        // 1. Configuration
        /////

        // Set default server URL
        Cfg.Sources.Insert(0,
            new MemoryConfigurationSource {
                InitialData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
                    { WebHostDefaults.ServerUrlsKey, AppHost.ServerUrls },
                },
            });
        // Disable FSW, because they eat a lot and can exhaust the handles available to epoll on linux containers
        var jsonProviders = Cfg.Sources.OfType<JsonConfigurationSource>().Where(j => j.ReloadOnChange).ToList();
        foreach (var item in jsonProviders) {
            Cfg.Sources.Remove(item);
            Cfg.AddJsonFile(item.Path!, item.Optional, reloadOnChange: false);
        }
        // Add a few default sources
        Cfg.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        Cfg.AddEnvironmentVariables();
        AppHost.Configure?.Invoke(this, Cfg);

        /////
        // 2. Base services
        /////

        var hostSettings = Cfg.GetSettings<HostSettings>();
        var appKind = hostSettings.AppKind ?? HostKind.Server;
        var isTested = hostSettings.IsTested ?? false;

        // Logging
        Services.AddLogging(logging => {
            logging.ClearProviders();
            if (configurationOnly)
                return;

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
        var roles = HostRoles.Server.GetAllRoles(serverRole, isTested);

        // HostInfo
        Services.AddSingleton(c => {
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
                Roles = roles,
                IsTested = isTested,
                BaseUrl = baseUrl,
            };
        });
        AppHost.ConfigureModuleHostServices?.Invoke(this, Services);

        /////
        // 3. ModuleHost & module service
        /////

        // Notice that we create "partial" service provider here, which contains
        // just the services registered above - i.e. logging + HostInfo
        ModuleServices = new DefaultServiceProviderFactory().CreateServiceProvider(Services);
        Log.IfEnabled(LogLevel.Information)
            ?.LogInformation("HostInfo: {HostInfo}", HostInfo);

        if (configurationOnly)
            ModuleHost = new ModuleHostBuilder().Build(Services);
        else {
            ModuleHost = new ModuleHostBuilder().WithModules(
                // From less dependent to more dependent!
                // Core modules
                new CoreModule(ModuleServices),
                new CoreServerModule(ModuleServices),
                new KubernetesModule(ModuleServices),
                new RedisModule(ModuleServices),
                new DbModule(ModuleServices),
                // API modules
                new ApiModule(ModuleServices),
                // Service-specific & service modules
                new StreamingServiceModule(ModuleServices),
                new MediaServiceModule(ModuleServices),
                new ContactsServiceModule(ModuleServices),
                new InviteServiceModule(ModuleServices),
                new UsersServiceModule(ModuleServices),
                new ChatServiceModule(ModuleServices),
                new NotificationServiceModule(ModuleServices),
                new SearchServiceModule(ModuleServices),
                // UI modules
                new BlazorUICoreModule(ModuleServices),
                new StreamingBlazorUIModule(ModuleServices),
                new UsersBlazorUIModule(ModuleServices),
                new ContactsBlazorUIModule(ModuleServices),
                new ChatBlazorUIModule(ModuleServices),
                new NotificationBlazorUIModule(ModuleServices),
                new BlazorUIAppModule(ModuleServices), // Should be the last one in UI section
                // This module should be the last one
                new AppServerModule(ModuleServices)
            ).Build(Services);
            AppHost.ConfigureServices?.Invoke(this, Services);
            if (HostInfo.IsDevelopmentInstance)
                ValidateContainerRegistrations();
        }

        /////
        // 4. Configure & build WebApplication (IHost)
        /////

        Builder.WebHost
            .UseDefaultServiceProvider((ctx, options) => {
                if (ctx.HostingEnvironment.IsDevelopment()) {
                    options.ValidateScopes = true;
                    options.ValidateOnBuild = true;
                }
            })
            .UseKestrel();
        App = Builder.Build();
        if (configurationOnly)
            return;

        /////
        // 5. Configure app
        /////

        var appHostModule = ModuleHost.GetModule<AppServerModule>();
        appHostModule.ConfigureApp(App); // This module must be the first one in ConfigureApp call sequence
        ModuleHost.Modules
            .Where(m => m.IsUsed)
            .OfType<IWebServerModule>()
            .Where(m => !ReferenceEquals(m, appHostModule))
            .Apply(m => m.ConfigureApp(App));
        AppHost.ConfigureApp?.Invoke(this, App);
    }

    private void ValidateContainerRegistrations()
    {
        var transientDisposables = Services.Where(x => x.Lifetime == ServiceLifetime.Transient)
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
}
