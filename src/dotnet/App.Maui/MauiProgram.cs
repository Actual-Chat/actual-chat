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
            var app = CreateMauiAppInternal();
            step.Close();
            LoadingUI.ReportMauiAppBuildTime(_tracer.Elapsed);
            return app;
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Failed to build actual.chat maui app");
            throw;
        }
    }

    private static LoggerConfiguration CreateLoggerConfiguration()
        => new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("ActualChat.UI.Blazor.Services.PersistentStorageReplicaCache", LogEventLevel.Debug)
            .MinimumLevel.Override("ActualChat.UI.Blazor.Services.ReplicaCacheStoragePerfMonitor", LogEventLevel.Debug)
            .WriteTo.Sentry(options => options.ConfigureForApp())
            .Enrich.With(new ThreadIdEnricher())
            .Enrich.FromLogContext()
            .Enrich.WithProperty(Serilog.Core.Constants.SourceContextPropertyName, "app.maui")
            .ConfigurePlatformLogger();

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

    private static MauiApp CreateMauiAppInternal()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSentry(options => options.ConfigureForApp())
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

        services.TryAddSingleton(builder.Configuration);
        services.AddPlatformServices();

        services.AddSingleton(new TracerProvider(_tracer));
        if (_tracer.IsEnabled) {
            // Use AddDispatcherProxy only to research purpose
            // MauiProgramOptimizations.AddDispatcherProxy(services, false);
        }

        var settings = new ClientAppSettings();
        _ = GetSessionId()
            .ContinueWith(t => settings.SetSessionId(t.Result), TaskScheduler.Default);
        services.TryAddSingleton(settings);

#if IS_FIXED_ENVIRONMENT_PRODUCTION || !(DEBUG || DEBUG_MAUI)
        var environment = Environments.Production;
#else
        var environment = Environments.Development;
#endif

        const string baseUrl = "https://" + MauiConstants.Host + "/";
        var initSessionInfoTask = InitSessionInfo(settings, new BaseUrlProvider(baseUrl));
        services.AddSingleton(c => new HostInfo {
            AppKind = AppKind.MauiApp,
            Environment = environment,
            Configuration = c.GetRequiredService<IConfiguration>(),
            BaseUrl = baseUrl,
            Platform = PlatformInfoProvider.GetPlatform(),
        });

        builder.ConfigureMauiHandlers(handlers => {
            handlers.AddHandler<IBlazorWebView, MauiBlazorWebViewHandler>();
        });

        var step = _tracer.Region("ConfigureServices");
        ConfigureServices(services);
        step.Close();

        step = _tracer.Region("Building maui app");
        var mauiApp = builder.Build();
        step.Close();

        AppServices = mauiApp.Services;

        //_ = MauiProgramOptimizations.WarmupFusionServices(AppServices, _tracer);

        Constants.HostInfo = AppServices.GetRequiredService<HostInfo>();
        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = AppServices.LogFor(typeof(WebMReader));

        AwaitInitSessionInfoTask(initSessionInfoTask);

        // MAUI does not start HostedServices, so we do this manually.
        // https://github.com/dotnet/maui/issues/2244
        step = _tracer.Region("Starting host services");
        StartHostedServices(mauiApp);
        step.Close();

        return mauiApp;
    }

    private static void AwaitInitSessionInfoTask(Task initSessionInfoTask)
        => initSessionInfoTask.GetAwaiter().GetResult();

    private static ILoggingBuilder ConfigureLogging(ILoggingBuilder logging, bool disposeSerilog)
    {
        var minLevel = Log.Logger.IsEnabled(LogEventLevel.Debug) ? LogLevel.Debug : LogLevel.Information;
        return logging
            .AddSerilog(Log.Logger, dispose: disposeSerilog)
            .SetMinimumLevel(minLevel);
    }

    private static Task InitSessionInfo(ClientAppSettings appSettings, BaseUrlProvider baseUrlProvider)
        => BackgroundTask.Run(async () => {
            var step = _tracer.Region("Init session info");
            var services = new ServiceCollection()
                .AddLogging(logging => ConfigureLogging(logging, false))
                .BuildServiceProvider();
            var log = services.GetRequiredService<ILogger<MauiApp>>();
            try {
                // Manually configure http client as we don't have it configured globally at DI level
                using var httpClient = new HttpClient(new HttpClientHandler {
                    SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    UseCookies = false,
                }, true) {
                    DefaultRequestVersion = HttpVersion.Version30,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                };
                httpClient.DefaultRequestHeaders.Add("cookie", $"GCLB=\"{AppStartup.SessionAffinityKey}\"");

                var log2 = services.GetRequiredService<ILogger<MobileAuthClient>>();
                var mobileAuthClient = new MobileAuthClient(appSettings, baseUrlProvider, httpClient, log2);
                log.LogInformation("Creating session...");
                if (!await mobileAuthClient.SetupSession().ConfigureAwait(false))
                    throw StandardError.StateTransition(nameof(MauiProgram), "Can not setup session");

                log.LogInformation("Creating session... Completed");
            }
            catch (Exception e) {
                log.LogError(e, "Failed to create session");
            }
            finally {
                await services.DisposeAsync().ConfigureAwait(false);
            }
            step.Close();
        });

    private static void StartHostedServices(MauiApp mauiApp)
    {
        var startTask = mauiApp.Services.HostedServices().Start();
        // Sync wait is on purpose - CreateMauiApp is synchronous!
        startTask.GetAwaiter().GetResult();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // HttpClient
#if !WINDOWS
        services.RemoveAll<IHttpClientFactory>();
        services.AddSingleton(c => new NativeHttpClientFactory(c));
        services.AddSingleton<IHttpClientFactory>(c => c.GetRequiredService<NativeHttpClientFactory>());
        services.AddSingleton<IHttpMessageHandlerFactory>(c => c.GetRequiredService<NativeHttpClientFactory>());
#endif
        AppStartup.ConfigureServices(services, AppKind.MauiApp, typeof(Module.BlazorUIClientAppModule)).Wait();

        // Auth
        services.AddScoped<IClientAuth>(c => new MauiClientAuth(c));
        services.AddSingleton<BaseUrlProvider>(c => new BaseUrlProvider(
            c.GetRequiredService<UrlMapper>().BaseUrl));
        services.AddTransient<MobileAuthClient>(c => new MobileAuthClient(
            c.GetRequiredService<ClientAppSettings>(),
            c.GetRequiredService<BaseUrlProvider>(),
            c.GetRequiredService<HttpClient>(),
            c.GetRequiredService<ILogger<MobileAuthClient>>()));

        // UI
        services.AddSingleton<NavigationInterceptor>(c => new NavigationInterceptor(c));
        services.AddTransient<MainPage>();
        services.AddScoped<KeepAwakeUI>(c => new MauiKeepAwakeUI(c));

        ActualChat.UI.Blazor.JSObjectReferenceExt.TestIfIsDisconnected = JSObjectReferenceDisconnectHelper.TestIfIsDisconnected;
        // Misc.
        services.AddScoped<DisposeTracer>(c => new DisposeTracer(c));
    }

    private static Task<Symbol> GetSessionId()
        => BackgroundTask.Run(async () => {
            var step = _tracer.Region("Getting session id");
            Symbol sessionId = Symbol.Empty;
            const string sessionIdStorageKey = "Fusion.SessionId";
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
                bool saved = false;
                try {
                    if (storage.Remove(sessionIdStorageKey))
                        Log.Information("Removed stored Session ID");
                    else
                        Log.Information("Did not Remove stored Session ID");
                    await storage.SetAsync(sessionIdStorageKey, sessionId.Value).ConfigureAwait(false);
                    saved = true;
                }
                catch (Exception e) {
                    saved = false;
                    Log.Warning(e, "Failed to store Session ID");
                    // ignored
                    // https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
                }
                if (!saved) {
                    Log.Information("Second attempt to store Session ID");
                    try {
                        storage.RemoveAll();
                        await storage.SetAsync(sessionIdStorageKey, sessionId.Value).ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        Log.Warning(e, "Failed to store Session ID second time");
                        // ignored
                        // https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
                    }
                }
            }
            step.Close();
            return sessionId;
        });

    private static partial void AddPlatformServices(this IServiceCollection services);
    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events);
    private static partial LoggerConfiguration ConfigurePlatformLogger(this LoggerConfiguration loggerConfiguration);
}
