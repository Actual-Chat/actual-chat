using System.Net;
using System.Security.Authentication;
using ActualChat.Hosting;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ActualChat.UI.Blazor.App;
using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Hosting;
using ActualChat.Audio.WebM;
using Microsoft.Maui.LifecycleEvents;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace ActualChat.App.Maui;

 #pragma warning disable VSTHRD002

public static partial class MauiProgram
{
    private static Tracer _tracer = null!;

    public static MauiApp CreateMauiApp()
    {
        Log.Logger = CreateLoggerConfiguration().CreateLogger();
        Tracer.Default = _tracer = CreateTracer();
        using var _1 = _tracer.Region(nameof(CreateMauiApp));
#if WINDOWS
        if (_tracer.IsEnabled) {
            // EventSources and EventListeners do not work in Mono. So no sense to enable but platforms different from Windows
            // MauiBlazorOptimizer.EnableDependencyInjectionEventListener();
        }
#endif

        var defaultLog = Log.Logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "@default");
        DefaultLog = new SerilogLoggerProvider(defaultLog).CreateLogger("MauiApp");
        AdjustThreadPool();
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        try {
            const string baseUrl = "https://" + MauiConstants.Host + "/";
            var settings = CreateClientAppSettings(baseUrl);
            var miniApp = CreateMauiMiniApp(settings);
            var configuration = miniApp.Configuration;
            var loggerFactory = miniApp.Services.GetRequiredService<ILoggerFactory>();
            var whenAppServicesReady = Task.Run(() => CreateAppServices(configuration, loggerFactory, settings));
            var appServices = new CompositeServiceProvider(
                miniApp.Services,
                whenAppServicesReady,
                CreateLazyServiceFilter(),
                miniApp);
            AppServices = appServices;
            LoadingUI.MarkMauiAppBuilt(_tracer.Elapsed);
            return (MauiApp)typeof(MauiApp)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .First()
                .Invoke(new object[] { appServices });
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Failed to build actual.chat maui app");
            throw;
        }
    }

    private static Tracer CreateTracer()
    {
#if DEBUG
        var logger = Log.Logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "@trace");
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        return new Tracer("MauiApp", x => logger.Information(x.Format()));
#else
        return Tracer.None;
#endif
    }

    private static LoggerConfiguration CreateLoggerConfiguration()
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("ActualChat.UI.Blazor.Services.AppReplicaCache", LogEventLevel.Debug)
            .Enrich.With(new ThreadIdEnricher())
            .Enrich.FromLogContext()
            .Enrich.WithProperty(Serilog.Core.Constants.SourceContextPropertyName, "app.maui")
            .ConfigurePlatformLogger();
        if (Constants.Sentry.EnabledFor.Contains(AppKind.MauiApp))
            configuration = configuration.WriteTo.Sentry(options => options.ConfigureForApp());
#if WINDOWS
        var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
        var timeSuffix = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var fileName = $"actual.chat.{timeSuffix}.log";
        configuration = configuration.WriteTo.File(
            Path.Combine(localFolder.Path, "Logs", fileName),
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
            fileSizeLimitBytes: 20 * 1024 * 1024);
#endif
        return configuration;
    }

    private static ClientAppSettings CreateClientAppSettings(string baseUrl)
    {
        var settings = new ClientAppSettings(baseUrl);
        _ = GetSession().ContinueWith(t => settings.Session = t.Result, TaskScheduler.Default);
        return settings;
    }

    private static MauiApp CreateMauiMiniApp(ClientAppSettings settings)
    {
        using var _ = _tracer.Region(nameof(CreateMauiMiniApp));

        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        if (Constants.Sentry.EnabledFor.Contains(AppKind.MauiApp))
            builder = builder.UseSentry(options => options.ConfigureForApp());

        builder = builder
            .ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            })
            .ConfigureLifecycleEvents(ConfigurePlatformLifecycleEvents)
            .UseAppLinks();

        var services = builder.Services;
        services.AddMauiBlazorWebView();

// Temporarily allow developer tools for all configurations
// #if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
// #endif

        services.AddLogging(logging => {
            // Remove direct Sentry logger provider.
            // Sentry logging will go indirectly via Serilog logger provider with Sentry sink.
            logging.ClearProviders();
            ConfigureLogging(logging, true);
        });

        services.AddSingleton(settings);
        services.AddTransient(_ => new MainPage(new NavigationInterceptor(settings)));
        if (_tracer.IsEnabled) {
            // Use UseDispatcherProxy only for debugging purposes
            // MauiBlazorOptimizer.UseDispatcherProxy(services, false);
        }

        builder.ConfigureMauiHandlers(handlers => {
            handlers.AddHandler<IBlazorWebView, MauiBlazorWebViewHandler>();
        });
        var mauiApp = builder.Build();
        return mauiApp;
    }

    private static void ConfigureLogging(ILoggingBuilder logging, bool disposeSerilog)
    {
        var minLevel = Log.Logger.IsEnabled(LogEventLevel.Debug) ? LogLevel.Debug : LogLevel.Information;
        logging
            .AddSerilog(Log.Logger, dispose: disposeSerilog)
            .SetMinimumLevel(minLevel);
    }

    private static IServiceProvider CreateAppServices(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        ClientAppSettings settings)
    {
        using var _1 = _tracer.Region(nameof(CreateAppServices));

        var services = new ServiceCollection();
        services.AddSingleton(new ScopedTracerProvider(_tracer)); // We don't want to have scoped tracers in MAUI app
        services.AddSingleton(configuration);
        // Register ILoggerFactory and ILogger
        services.AddSingleton(loggerFactory);
        services.Add(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Logger<>)));
        services.AddSingleton(settings);

        RegisterNonLazyServicesVisibleFromLazyServices(services);

#if IS_FIXED_ENVIRONMENT_PRODUCTION || !DEBUG
        var environment = Environments.Production;
#else
        var environment = Environments.Development;
#endif

        // Start SetupSession
        var whenSessionReady = SetupSession(settings, loggerFactory);

        // Add HostInfo
        var hostInfo = new HostInfo {
            AppKind = AppKind.MauiApp,
            Environment = environment,
            Configuration = configuration,
            BaseUrl = settings.BaseUrl,
            Platform = PlatformInfoProvider.GetPlatform(),
        };
        Constants.HostInfo = hostInfo;
        services.AddSingleton(_ => hostInfo);

        // Add platform services
        services.AddPlatformServices();

        // Configure the rest
        ConfigureAppServices(services);

        // Build IServiceProvider
        var appServices = services.BuildServiceProvider();
        var appServiceStarter = appServices.GetRequiredService<AppServiceStarter>();
        _ = appServiceStarter.PreWebViewWarmup(CancellationToken.None);

        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = loggerFactory.CreateLogger(typeof(WebMReader));

        CompleteSetupSession(whenSessionReady);
        return appServices;
    }

    private static void RegisterNonLazyServicesVisibleFromLazyServices(IServiceCollection services)
    {
        // The services listed below are resolved by other services from lazy service provider,
        // and since it doesn't have them registered (inside the actual service provider
        // backing the lazy one), they won't be resolved unless we re-register them somehow.
        var externallyResolvedTypes = new [] {
            typeof(Microsoft.JSInterop.IJSRuntime),
            typeof(Microsoft.AspNetCore.Components.Routing.INavigationInterception),
            typeof(Microsoft.AspNetCore.Components.NavigationManager),
            typeof(Microsoft.AspNetCore.Components.Web.IErrorBoundaryLogger),
        };

        services.AddScoped(_ => new NonLazyServiceAccessor());

        foreach (var serviceType in externallyResolvedTypes) {
            var serviceDescriptor = ServiceDescriptor.Scoped(serviceType, ImplementationFactory);
            services.Add(serviceDescriptor);

            object ImplementationFactory(IServiceProvider c) {
                var accessor = c.GetRequiredService<NonLazyServiceAccessor>();
                var result = accessor.GetService(serviceType);
                if (result == null)
                    throw StandardError.Internal($"Couldn't resolve non-lazy service: '{serviceType.GetName()}'.");

                return result;
            }
        }
    }

    private static Task SetupSession(ClientAppSettings appSettings, ILoggerFactory loggerFactory)
        => BackgroundTask.Run(async () => {
            var _ = _tracer.Region(nameof(SetupSession));
            var log = loggerFactory.CreateLogger<MauiApp>();
            try {
                // Manually configure HTTP client as we don't have it configured globally at DI level
                using var httpClient = new HttpClient(new HttpClientHandler {
                    SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    UseCookies = false,
                }, true) {
                    DefaultRequestVersion = HttpVersion.Version30,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                };
                httpClient.DefaultRequestHeaders.Add("cookie", AppStartup.GetCookieHeader());

                var log2 = loggerFactory.CreateLogger<MobileAuthClient>();
                var mobileAuthClient = new MobileAuthClient(appSettings, httpClient, log2);
                if (!await mobileAuthClient.SetupSession().ConfigureAwait(false))
                    throw StandardError.StateTransition(nameof(MauiProgram), "Couldn't setup Session!");
            }
            catch (Exception e) {
                log.LogError(e, "Failed to setup Session");
            }
        });

    private static void CompleteSetupSession(Task whenSessionReady)
    {
        using var _ = _tracer.Region(nameof(CompleteSetupSession));
        whenSessionReady.Wait();
    }

    private static void ConfigureAppServices(IServiceCollection services)
    {
        using var _ = _tracer.Region(nameof(ConfigureAppServices));

        // HttpClient
#if !WINDOWS
        services.RemoveAll<IHttpClientFactory>();
        services.AddSingleton(c => new NativeHttpClientFactory(c));
        services.AddSingleton<IHttpClientFactory>(c => c.GetRequiredService<NativeHttpClientFactory>());
        services.AddSingleton<IHttpMessageHandlerFactory>(c => c.GetRequiredService<NativeHttpClientFactory>());
#endif
        AppStartup.ConfigureServices(services, AppKind.MauiApp, c => new HostModule[] {
            new Module.BlazorUIClientAppModule(c),
        });

        // Auth
        services.AddScoped<IClientAuth>(c => new MauiClientAuth(c));
        services.AddSingleton<BaseUrlProvider>(c => new BaseUrlProvider(
            c.GetRequiredService<UrlMapper>().BaseUrl));
        services.AddTransient<MobileAuthClient>(c => new MobileAuthClient(
            c.GetRequiredService<ClientAppSettings>(),
            c.GetRequiredService<HttpClient>(),
            c.GetRequiredService<ILogger<MobileAuthClient>>()));

        // UI
        services.AddScoped<KeepAwakeUI>(c => new MauiKeepAwakeUI(c));

        JSObjectReferenceExt.TestIfDisconnected = JSObjectReferenceDisconnectHelper.TestIfIsDisconnected;
        // Misc.
        services.AddScoped<DisposeTracer>(c => new DisposeTracer(c));
    }

    private static Task<Session> GetSession()
        => BackgroundTask.Run(async () => {
            using var _ = _tracer.Region(nameof(GetSession));

            const string sessionIdStorageKey = "Fusion.SessionId";
            var session = (Session?)null;

            var storage = SecureStorage.Default;
            try {
                var storedSessionId = await storage.GetAsync(sessionIdStorageKey).ConfigureAwait(false);
                if (!storedSessionId.IsNullOrEmpty()) {
                    session = new Session(storedSessionId);
                    Log.Information("Successfully read stored Session ID");
                }
                else
                    Log.Information("No stored Session ID");
            }
            catch (Exception e) {
                Log.Warning(e, "Failed to read stored Session ID");
                // ignored
                // https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
                // TODO: configure selective backup, to prevent app crashes after re-installing
                // https://learn.microsoft.com/en-us/xamarin/essentials/secure-storage?tabs=android#selective-backup
            }

            if (session == null) {
                session = new SessionFactory().CreateSession();
                bool isSaved;
                try {
                    if (storage.Remove(sessionIdStorageKey))
                        Log.Information("Removed stored Session ID");
                    await storage.SetAsync(sessionIdStorageKey, session.Id.Value).ConfigureAwait(false);
                    isSaved = true;
                }
                catch (Exception e) {
                    isSaved = false;
                    Log.Warning(e, "Failed to store Session ID");
                    // Ignored, see:
                    // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
                }

                if (!isSaved) {
                    Log.Information("Second attempt to store Session ID");
                    try {
                        storage.RemoveAll();
                        await storage.SetAsync(sessionIdStorageKey, session.Id.Value).ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        Log.Warning(e, "Failed to store Session ID (second attempt)");
                        // Ignored, see:
                        // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
                    }
                }
            }

            return session;
        });

    private static Func<Type, bool> CreateLazyServiceFilter()
    {
        // The services listed here will always resolve to null if they're requested
        // via composite service provider. In fact, these services are optional
        // and aren't supposed to be used, but an attempt to resolve them triggers
        // the build of lazy service provider, so we must explicitly filter them out.
        var servicesToSkip = new HashSet<Type> {
            typeof(Microsoft.AspNetCore.Components.IComponentActivator),
            typeof(IEnumerable<IMauiInitializeScopedService>),
        };
        AddPlatformServicesToSkip(servicesToSkip);
        return serviceType => !servicesToSkip.Contains(serviceType);
    }

    private static partial void AddPlatformServices(this IServiceCollection services);
    private static partial void AddPlatformServicesToSkip(HashSet<Type> servicesToSkip);
    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events);
    private static partial LoggerConfiguration ConfigurePlatformLogger(this LoggerConfiguration loggerConfiguration);

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => Log.Information("Unhandled exception, isTerminating={IsTerminating}. \n{Exception}",
            e.IsTerminating,
            e.ExceptionObject);

    private static void AdjustThreadPool()
    {
        ThreadPool.GetMinThreads(out var min, out var minIO);
        ThreadPool.GetMaxThreads(out var max, out var maxIO);
        var cpuCount = HardwareInfo.ProcessorCount;
        _tracer.Point($"{nameof(AdjustThreadPool)} - original settings: ({min}, {minIO}) .. ({max}, {maxIO}), CPU count: {cpuCount}");

        min = cpuCount * 4;
        minIO = cpuCount * 4;
        ThreadPool.SetMinThreads(min, minIO);
        ThreadPool.GetMinThreads(out min, out minIO);
        _tracer.Point($"{nameof(AdjustThreadPool)} - new settings: ({min}, {minIO}) .. ({max}, {maxIO})");
    }
}
