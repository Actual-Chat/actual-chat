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
using Serilog;
using Serilog.Events;

namespace ActualChat.App.Maui;

 #pragma warning disable VSTHRD002

public static partial class MauiProgram
{
    private static Tracer _tracer = null!;

    public static MauiApp CreateMauiApp()
    {
        Log.Logger = CreateLoggerConfiguration().CreateLogger();
        Tracer.Default = _tracer = CreateTracer();
        _tracer.Point("Tracer and Logger are ready");

#if WINDOWS
        if (_tracer.IsEnabled) {
            // EventSources and EventListeners do not work in Mono. So no sense to enable but platforms different from Windows
            // MauiProgramOptimizations.EnableDependencyInjectionEventListener();
        }
#endif

        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;

        try {
            using var step = _tracer.Region("Building MAUI app");
            const string baseUrl = "https://" + MauiConstants.Host + "/";
            var settings = CreateClientAppSettings(baseUrl);
            var coreApp = CreateCoreMauiApp(settings);
            var configuration = coreApp.Configuration;
            var loggerFactory = coreApp.Services.GetRequiredService<ILoggerFactory>();
            var appServicesTask = Task.Run(() => CreateBlazorAppServices(configuration, loggerFactory, settings));
            var filter = CreateBlazorServicesLookupFilter();
            var svpWrapper = new CompositeBlazorHybridServiceProvider(coreApp, appServicesTask, filter);
            AppServices = svpWrapper;

            appServicesTask.ContinueWith(_ => {
                // MAUI does not start HostedServices, so we do this manually.
                // https://github.com/dotnet/maui/issues/2244
                var step1 = _tracer.Region("Starting host services");
                StartHostedServices(svpWrapper);
                step1.Close();
            }, TaskScheduler.Default);

            LoadingUI.ReportMauiAppBuildTime(_tracer.Elapsed);
            return CreateMauiApp(svpWrapper);
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Failed to build actual.chat maui app");
            throw;
        }
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

    private static Tracer CreateTracer()
    {
#if DEBUG || DEBUG_MAUI
        var logger = Log.Logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "@trace");
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        return new Tracer("MauiApp", x => logger.Information(x.Format()));
#else
        return Tracer.None;
#endif
    }

    private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => Log.Information("Unhandled exception, isTerminating={IsTerminating}. \n{Exception}",
            e.IsTerminating,
            e.ExceptionObject);

    private static MauiApp CreateCoreMauiApp(ClientAppSettings settings)
    {
        var stepOverall = _tracer.Region("Building maui app service provider");

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
// #if DEBUG || DEBUG_MAUI
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
            // Use AddDispatcherProxy only to research purpose
            // MauiProgramOptimizations.AddDispatcherProxy(services, false);
        }
        builder.ConfigureMauiHandlers(handlers => {
            handlers.AddHandler<IBlazorWebView, MauiBlazorWebViewHandler>();
        });
        var mauiApp = builder.Build();
        stepOverall.Close();
        return mauiApp;
    }

    private static IServiceProvider CreateBlazorAppServices(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        ClientAppSettings settings)
    {
        var stepOverall = _tracer.Region("Building blazor app service provider");
        var services = new ServiceCollection();
        services.AddSingleton(new TracerProvider(_tracer));
        services.AddSingleton(configuration);
        // Register ILoggerFactory and ILogger
        services.AddSingleton(loggerFactory);
        services.Add(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Logger<>)));
        services.AddSingleton(settings);

        RegisterExternalBlazorWebViewServices(services);

#if IS_FIXED_ENVIRONMENT_PRODUCTION || !(DEBUG || DEBUG_MAUI)
        var environment = Environments.Production;
#else
        var environment = Environments.Development;
#endif

        var initSessionInfoTask = InitSessionInfo(settings, loggerFactory);
        var hostInfo = new HostInfo {
            AppKind = AppKind.MauiApp,
            Environment = environment,
            Configuration = configuration,
            BaseUrl = settings.BaseUrl,
            Platform = PlatformInfoProvider.GetPlatform(),
        };
        Constants.HostInfo = hostInfo;
        services.AddSingleton(_ => hostInfo);

        services.AddPlatformServices();

        var step = _tracer.Region("ConfigureServices");
        ConfigureServices(services);
        step.Close();

        var appServices = services.BuildServiceProvider();

        //_ = MauiProgramOptimizations.WarmupFusionServices(AppServices, _tracer);

        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = loggerFactory.CreateLogger(typeof(WebMReader));

        step = _tracer.Region("AwaitInitSessionInfoTask");
        AwaitInitSessionInfoTask(initSessionInfoTask);
        step.Close();

        stepOverall.Close();

        return appServices;
    }

    private static void RegisterExternalBlazorWebViewServices(IServiceCollection services)
    {
        // we have to resolve below services from maui app services provider
        var externalResolveTypes = new [] {
            typeof(Microsoft.JSInterop.IJSRuntime),
            typeof(Microsoft.AspNetCore.Components.Routing.INavigationInterception),
            typeof(Microsoft.AspNetCore.Components.NavigationManager),
            typeof(Microsoft.AspNetCore.Components.Web.IErrorBoundaryLogger),
        };

        services.AddScoped<DelegateServiceResolver>();

        foreach (var serviceType in externalResolveTypes) {
            var serviceDescriptor = ServiceDescriptor.Scoped(serviceType, ImplementationFactory);
            services.Add(serviceDescriptor);

            object ImplementationFactory(IServiceProvider c) {
                var resolver = c.GetRequiredService<DelegateServiceResolver>();
                var result = resolver.GetService(serviceType);
                if (result == null)
                    throw StandardError.Constraint($"Can't resolve blazor web view service: '{serviceType}'");
                return result;
            }
        }
    }

    private static ClientAppSettings CreateClientAppSettings(string baseUrl)
    {
        var settings = new ClientAppSettings(baseUrl);
        _ = GetSessionId()
            .ContinueWith(t => settings.SessionId = t.Result, TaskScheduler.Default);
        return settings;
    }

    private static void AwaitInitSessionInfoTask(Task initSessionInfoTask)
        => initSessionInfoTask.GetAwaiter().GetResult();

    private static void ConfigureLogging(ILoggingBuilder logging, bool disposeSerilog)
    {
        var minLevel = Log.Logger.IsEnabled(LogEventLevel.Debug) ? LogLevel.Debug : LogLevel.Information;
        logging
            .AddSerilog(Log.Logger, dispose: disposeSerilog)
            .SetMinimumLevel(minLevel);
    }

    private static Task InitSessionInfo(ClientAppSettings appSettings, ILoggerFactory loggerFactory)
        => BackgroundTask.Run(async () => {
            var step = _tracer.Region("Init session info");
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
                log.LogInformation("Creating session...");
                if (!await mobileAuthClient.SetupSession().ConfigureAwait(false))
                    throw StandardError.StateTransition(nameof(MauiProgram), "Can not setup session");

                log.LogInformation("Creating session... Completed");
            }
            catch (Exception e) {
                log.LogError(e, "Failed to create session");
            }
            step.Close();
        });

    private static void StartHostedServices(IServiceProvider services)
        => _ = services.HostedServices().Start();

    private static void ConfigureServices(IServiceCollection services)
    {
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

    private static Task<Symbol> GetSessionId()
        => BackgroundTask.Run(async () => {
            const string sessionIdStorageKey = "Fusion.SessionId";
            Symbol sessionId = Symbol.Empty;

            var step = _tracer.Region("Getting session id");
            Log.Information("About to read stored Session ID");
            var storage = SecureStorage.Default;
            try {
                var storedSessionId = await storage.GetAsync(sessionIdStorageKey).ConfigureAwait(false);
                if (!storedSessionId.IsNullOrEmpty()) {
                    sessionId = storedSessionId;
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
            if (sessionId.IsEmpty) {
                sessionId = new SessionFactory().CreateSession().Id;
                bool isSaved;
                try {
                    if (storage.Remove(sessionIdStorageKey))
                        Log.Information("Removed stored Session ID");
                    else
                        Log.Information("Did not Remove stored Session ID");
                    await storage.SetAsync(sessionIdStorageKey, sessionId.Value).ConfigureAwait(false);
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
                        await storage.SetAsync(sessionIdStorageKey, sessionId.Value).ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        Log.Warning(e, "Failed to store Session ID second time");
                        // Ignored, see:
                        // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
                    }
                }
            }
            step.Close();
            return sessionId;
        });

    private static Func<Type, bool> CreateBlazorServicesLookupFilter()
    {
        // To prevent lookup in blazor app service provider
        // Otherwise we can start awaiting too earlier that service provider is ready
        var servicesToSkip = new HashSet<Type> {
            typeof(Microsoft.AspNetCore.Components.IComponentActivator),
        };
        AddPlatformServicesToLookupSkipper(servicesToSkip);

        bool SkipBlazorServiceLookup(Type c)
            => servicesToSkip.Contains(c);

        return SkipBlazorServiceLookup;
    }

    private static MauiApp CreateMauiApp(IServiceProvider services)
    {
        var constructors = typeof(MauiApp).GetConstructors(
            BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.NonPublic
            | BindingFlags.Public);
        return (MauiApp)constructors.First().Invoke(new object[] { services });
    }

    private static partial void AddPlatformServices(this IServiceCollection services);
    private static partial void AddPlatformServicesToLookupSkipper(ISet<Type> servicesToSkip);
    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events);
    private static partial LoggerConfiguration ConfigurePlatformLogger(this LoggerConfiguration loggerConfiguration);
}
