using ActualChat.App.Server.Module;
using ActualChat.Audio.Module;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Chat.Module;
using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Contacts.Module;
using ActualChat.Contacts.UI.Blazor.Module;
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
using ActualChat.UI.Blazor.Module;
using ActualChat.Users.Module;
using ActualChat.Users.UI.Blazor.Module;
using ActualChat.Web.Module;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Logging.Console;
using Serilog;
using Serilog.Events;

namespace ActualChat.App.Server;

public class AppHost : IDisposable
{
    private volatile int _isDisposed;

    public string ServerUrls { get; set; } = "http://localhost:7080;https://localhost:7081";
    public Action<IConfigurationBuilder>? HostConfigurationBuilder { get; set; }
    public Action<WebHostBuilderContext, IServiceCollection>? AppServicesBuilder { get; set; }
    public Action<IConfigurationBuilder>? AppConfigurationBuilder { get; set; }

    public IHost Host { get; protected set; } = null!;
    public ModuleHost ModuleHost { get; set; } = null!;
    public IServiceProvider Services => Host.Services;

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
            return;

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            Host.DisposeSilently();
    }

    public Task Build(string[] args, CancellationToken cancellationToken = default)
    {
        var webBuilder = WebApplication.CreateBuilder(args);

        webBuilder.Host
            .ConfigureHostConfiguration(ConfigureHostConfiguration);
            // .ConfigureWebHostDefaults(builder => builder
            //     .UseDefaultServiceProvider((ctx, options) => {
            //         if (ctx.HostingEnvironment.IsDevelopment()) {
            //             options.ValidateScopes = true;
            //             options.ValidateOnBuild = true;
            //         }
            //     })
            //     .UseKestrel(ConfigureKestrel)
            //     .ConfigureAppConfiguration(ConfigureAppConfiguration)
            //     .UseStartup<Startup>()
            //     .ConfigureServices(ConfigureAppServices)
            //     .ConfigureServices(ValidateContainerRegistrations)
            // );
        webBuilder.WebHost
            .UseDefaultServiceProvider((ctx, options) => {
                if (!ctx.HostingEnvironment.IsDevelopment())
                    return;

                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            })
            .UseKestrel(ConfigureKestrel)
            .ConfigureAppConfiguration(ConfigureAppConfiguration)
            .ConfigureServices(ConfigureHost)
            .ConfigureServices(ConfigureAppServices)
            .ConfigureServices(ValidateContainerRegistrations);

        webBuilder.Services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .AddInteractiveWebAssemblyComponents();

        var webApplication = webBuilder.Build();
        ConfigureApp(webApplication);

        Host = webApplication;
        return Task.CompletedTask;
    }

    private void ValidateContainerRegistrations(WebHostBuilderContext webHostBuilderContext, IServiceCollection services)
    {
        if (!webHostBuilderContext.HostingEnvironment.IsDevelopment())
            return;

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

    public virtual async Task InvokeDbInitializers(CancellationToken cancellationToken = default)
    {
        // InitializeSchema
        await InvokeDbInitializers(
            nameof(IDbInitializer.InitializeSchema),
            (x, ct) => x.InitializeSchema(ct),
            cancellationToken
        ).ConfigureAwait(false);

        // InitializeData
        await InvokeDbInitializers(
            nameof(IDbInitializer.InitializeData),
            (x, ct) => x.InitializeData(ct),
            cancellationToken
        ).ConfigureAwait(false);

        // RepairData
        await InvokeDbInitializers(
            nameof(IDbInitializer.RepairData),
            x => x.ShouldRepairData,
            (x, ct) => x.RepairData(ct),
            cancellationToken
        ).ConfigureAwait(false);

        // VerifyData
        await InvokeDbInitializers(
            nameof(IDbInitializer.VerifyData),
            x => x.ShouldVerifyData,
            (x, ct) => x.VerifyData(ct),
            cancellationToken
        ).ConfigureAwait(false);
    }

    public virtual Task Run(CancellationToken cancellationToken = default)
        => Host.RunAsync(cancellationToken);

    public virtual Task Start(CancellationToken cancellationToken = default)
        => Host.StartAsync(cancellationToken);

    public virtual Task Stop(CancellationToken cancellationToken = default)
        => Host.StopAsync(cancellationToken);

    // Protected & private methods

    protected virtual void ConfigureHostConfiguration(IConfigurationBuilder cfg)
    {
        // Looks like there is no better way to set _default_ URL
        cfg.Sources.Insert(0,
            new MemoryConfigurationSource {
                InitialData = new Dictionary<string, string?>(StringComparer.Ordinal) {
                    { WebHostDefaults.ServerUrlsKey, ServerUrls },
                },
            });
        cfg.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        HostConfigurationBuilder?.Invoke(cfg);
    }

    protected void ConfigureKestrel(WebHostBuilderContext ctx, KestrelServerOptions options)
    { }

    protected virtual void ConfigureAppConfiguration(IConfigurationBuilder appBuilder)
    {
        // Disable FSW, because they eat a lot and can exhaust the handles available to epoll on linux containers
        var jsonProviders = appBuilder.Sources.OfType<JsonConfigurationSource>().Where(j => j.ReloadOnChange).ToArray();
        foreach (var item in jsonProviders) {
            appBuilder.Sources.Remove(item);
            appBuilder.AddJsonFile(item.Path!, item.Optional, reloadOnChange: false);
        }
        appBuilder.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        appBuilder.AddEnvironmentVariables();

        AppConfigurationBuilder?.Invoke(appBuilder);
    }

    protected virtual void ConfigureHost(WebHostBuilderContext webHost, IServiceCollection services)
    {
        var env = webHost.HostingEnvironment;
        var cfg = webHost.Configuration;
        var hostSettings = cfg.GetSettings<HostSettings>();
        var appKind = hostSettings.AppKind ?? AppKind.WebServer;
        var isTested = hostSettings.IsTested ?? false;

        // Logging
        services.AddLogging(logging => {
            logging.ClearProviders();
            logging.ConfigureServerFilters(env.EnvironmentName);
            logging.AddConsole();
            logging.AddConsoleFormatter<GoogleCloudConsoleFormatter, JsonConsoleFormatterOptions>();
            if (AppLogging.IsDevLogRequested && appKind.IsServer() && !isTested) { // This excludes TestServer
                var serilog = new LoggerConfiguration()
                    .MinimumLevel.Is(LogEventLevel.Verbose)
                    .Enrich.FromLogContext()
                    .Enrich.With(new ThreadIdEnricher())
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
                IsTested = isTested,
                ClientKind = ClientKind.Unknown,
                Environment = env.EnvironmentName,
                Configuration = cfg,
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
                new CoreServerModule(moduleServices),
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
                new ContactsBlazorUIModule(moduleServices),
                new ChatBlazorUIModule(moduleServices),
                new NotificationBlazorUIModule(moduleServices),
                new BlazorUIAppModule(moduleServices), // Should be the last one in UI section
                // This module should be the last one
                new AppServerModule(moduleServices)
            ).Build(services);
    }

    protected virtual void ConfigureAppServices(
        WebHostBuilderContext webHost,
        IServiceCollection services)
        => AppServicesBuilder?.Invoke(webHost, services);

    private void ConfigureApp(WebApplication app)
    {
        var appHostModule = ModuleHost.GetModule<AppServerModule>();
        appHostModule.ConfigureApp(app); // This module must be the first one in ConfigureApp call sequence

        ModuleHost.Modules
            .OfType<IWebModule>()
            .Where(m => !ReferenceEquals(m, appHostModule))
            .Apply(m => m.ConfigureApp(app));
    }

    private Task InvokeDbInitializers(
        string name,
        Func<IDbInitializer, CancellationToken, Task> invoker,
        CancellationToken cancellationToken)
        => InvokeDbInitializers(name, _ => true, invoker, cancellationToken);

    private async Task InvokeDbInitializers(
        string name,
        Func<IDbInitializer, bool> mustInvokePredicate,
        Func<IDbInitializer, CancellationToken, Task> invoker,
        CancellationToken cancellationToken)
    {
        var log = Host.Services.LogFor(GetType());
        var runningTaskSources = Host.Services.GetServices<IDbInitializer>()
            .ToDictionary(x => x, _ => TaskCompletionSourceExt.New<bool>());
        var runningTasks = runningTaskSources
            .ToDictionary(kv => kv.Key, kv => (Task)kv.Value.Task);
        foreach (var (dbInitializer, _) in runningTasks)
            dbInitializer.RunningTasks = runningTasks;
        var tasks = runningTaskSources
            .Select(kv => mustInvokePredicate.Invoke(kv.Key) ? InvokeOne(kv.Key, kv.Value) : Task.CompletedTask)
            .ToArray();

        try {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally {
            foreach (var (dbInitializer, _) in runningTasks)
                dbInitializer.RunningTasks = null!;
        }
        return;

        async Task InvokeOne(IDbInitializer dbInitializer, TaskCompletionSource<bool> initializedSource)
        {
            using var _ = dbInitializer.Activate();
            var dbInitializerName = $"{dbInitializer.GetType().GetName()}.{name}";
            try {
                using var _1 = Tracer.Default.Region(dbInitializerName);
                log.LogInformation("{DbInitializer} started", dbInitializerName);
                var task = invoker.Invoke(dbInitializer, cancellationToken);
                if (task.IsCompletedSuccessfully)
                    log.LogInformation("{DbInitializer} completed synchronously (skipped?)", dbInitializerName);
                else {
                    await task.ConfigureAwait(false);
                    log.LogInformation("{DbInitializer} completed", dbInitializerName);
                }
                initializedSource.TrySetResult(default);
            }
            catch (OperationCanceledException) {
                initializedSource.TrySetCanceled(cancellationToken);
                throw;
            }
            catch (Exception e) {
                log.LogError(e, "{DbInitializer} failed", dbInitializerName);
                initializedSource.TrySetException(e);
                throw;
            }
        }
    }
}
