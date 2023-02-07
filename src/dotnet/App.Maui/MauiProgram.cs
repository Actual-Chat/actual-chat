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
using ActualChat.Chat.UI.Blazor.Services;
using Microsoft.JSInterop;
using Serilog;
using Serilog.Events;

namespace ActualChat.App.Maui;

 #pragma warning disable VSTHRD002

public static class MauiProgram
{
    private static readonly ITraceSession _trace;

    static MauiProgram()
    {
        // Setup default trace session if it was not done earlier
        if (TraceSession.IsTracingEnabled && TraceSession.Default == TraceSession.Null)
            TraceSession.Default = TraceSession.New("main").Start();
        _trace = TraceSession.Default;
    }

    public static MauiApp CreateMauiApp()
    {
        _trace.Track("MauiProgram.CreateMauiApp");

        var loggerConfiguration = new LoggerConfiguration().MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext();
#if ANDROID
        loggerConfiguration = loggerConfiguration.WriteTo
            .AndroidTaggedLog(AndroidConstants.LogTag)
            .Enrich.WithProperty(Serilog.Core.Constants.SourceContextPropertyName, "app.maui");
#elif IOS
        loggerConfiguration = loggerConfiguration.WriteTo.NSLog();
#endif
        Log.Logger = loggerConfiguration.CreateLogger();
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;

        try {
            Log.Information("Starting to build actual.chat maui app");
            var app = CreateMauiAppInternal();
            Log.Information("Successfully built actual.chat maui app");
            return app;
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Failed to build actual.chat maui app");
            throw;
        }
    }

    private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Information("Unhandled exception, isTerminating={IsTerminating}. \n{Exception}",
            e.IsTerminating,
            e.ExceptionObject);
    }

    private static MauiApp CreateMauiAppInternal()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            })
            .ConfigureLifecycleEvents(ConfigureLifecycleEvents)
            .Logging.AddDebug();

        var services = builder.Services;
        services.AddMauiBlazorWebView();

// Temporarily allow developer tools for all configurations
// #if DEBUG || DEBUG_MAUI
        builder.Services.AddBlazorWebViewDeveloperTools();
// #endif

        services.AddLogging(logging => ConfigureLogging(logging, true));

        services.TryAddSingleton(builder.Configuration);
        services.TryAddSingleton<ITraceSession>(_trace);

        var settings = new ClientAppSettings();
        _ = GetSessionId()
            .ContinueWith(t => settings.SetSessionId(t.Result), TaskScheduler.Default);
        services.TryAddSingleton(settings);

#if IS_FIXED_ENVIRONMENT_PRODUCTION || !(DEBUG || DEBUG_MAUI)
        var environment = Environments.Production;
#else
        var environment = Environments.Development;
#endif

        var baseUrl = GetBaseUrl();
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

#if ANDROID
        services.AddSingleton<Java.Util.Concurrent.IExecutorService>(_ =>
            Java.Util.Concurrent.Executors.NewWorkStealingPool()!);
#endif
        var step = _trace.TrackStep("ConfigureServices");
        ConfigureServices(services);
        step.Complete();

        step = _trace.TrackStep("Building maui app");
        var mauiApp = builder.Build();
        step.Complete();

        AppServices = mauiApp.Services;

        Constants.HostInfo = AppServices.GetRequiredService<HostInfo>();
        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = AppServices.LogFor(typeof(WebMReader));

        initSessionInfoTask.GetAwaiter().GetResult();

        // MAUI does not start HostedServices, so we do this manually.
        // https://github.com/dotnet/maui/issues/2244
        step = _trace.TrackStep("Starting host services");
        StartHostedServices(mauiApp);
        step.Complete();

        return mauiApp;
    }

    private static ILoggingBuilder ConfigureLogging(ILoggingBuilder logging, bool disposeSerilog)
        => logging
            .AddDebug()
            .AddSerilog(Log.Logger, dispose: disposeSerilog)
            .SetMinimumLevel(LogLevel.Information);

    private static Task InitSessionInfo(ClientAppSettings appSettings, BaseUrlProvider baseUrlProvider)
        => BackgroundTask.Run(async () => {
            var step = _trace.TrackStep("Init session info");
            var services = new ServiceCollection()
                .AddLogging(logging => ConfigureLogging(logging, false))
                .BuildServiceProvider();
            var log = services.GetRequiredService<ILogger<MauiApp>>();
            try {
                var log2 = services.GetRequiredService<ILogger<MobileAuthClient>>();
                var mobileAuthClient = new MobileAuthClient(appSettings, baseUrlProvider, log2);
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
            step.Complete();
        });

    private static void StartHostedServices(MauiApp mauiApp)
        => mauiApp.Services.HostedServices().Start()
            .Wait(); // wait on purpose, CreateMauiApp is synchronous.

    private static string GetBaseUrl()
    {
#if ISDEVMAUI
        return "https://dev.actual.chat/";
#else
        return "https://actual.chat/";
#endif
    }

    private static void ConfigureLifecycleEvents(ILifecycleBuilder events)
    {
#if ANDROID
        events.AddAndroid(android => {
            android.OnBackPressed(activity => {
                _ = HandleBackPressed(activity);
                return true;
            });
        });
#endif
    }

#if ANDROID
    private static async Task HandleBackPressed(Android.App.Activity activity)
    {
        var webView = Application.Current?.MainPage is MainPage mainPage ? mainPage.PlatformWebView : null;
        var goBack = webView != null ? await TryGoBack(webView).ConfigureAwait(false) : false;
        if (goBack)
            return;
        // Move app to background as Home button acts.
        // It prevents scenario when app is running, but activity is destroyed.
        activity.MoveTaskToBack(true);
    }

    private static async Task<bool> TryGoBack(Android.Webkit.WebView webView)
    {
        var canGoBack = webView.CanGoBack();
        if (canGoBack) {
            webView.GoBack();
            return true;
        }
        // Sometimes Chromium reports that it can't go back while there are 2 items in the history.
        // It seems that this bug exists for a while, not fixed yet and there is not plans to do it.
        // https://bugs.chromium.org/p/chromium/issues/detail?id=1098388
        // https://github.com/flutter/flutter/issues/59185
        // But we can use web api to navigate back.
        var list = webView.CopyBackForwardList();
        var canGoBack2 = list.Size > 1 && list.CurrentIndex > 0;
        if (canGoBack2) {
            if (ScopedServicesAccessor.IsInitialized) {
                var jsRuntime = ScopedServicesAccessor.ScopedServices.GetRequiredService<IJSRuntime>();
                await jsRuntime.InvokeVoidAsync("eval", "history.back()").ConfigureAwait(false);
                return true;
            }
        }
        return false;
    }

#endif

    private static void ConfigureServices(IServiceCollection services)
    {
        // HttpClient
#if !WINDOWS
        services.RemoveAll<IHttpClientFactory>();
        services.AddSingleton<NativeHttpClientFactory>(c => new NativeHttpClientFactory(c));
        services.TryAddSingleton<IHttpClientFactory>(c => c.GetRequiredService<NativeHttpClientFactory>());
        services.TryAddSingleton<IHttpMessageHandlerFactory>(c => c.GetRequiredService<NativeHttpClientFactory>());
#endif
        AppStartup.ConfigureServices(services, typeof(Module.BlazorUIClientAppModule)).Wait();

        // Auth
        services.AddScoped<IClientAuth>(c => new MauiClientAuth(c));
        services.AddSingleton<BaseUrlProvider>(c => new BaseUrlProvider(
            c.GetRequiredService<UrlMapper>().BaseUrl));
        services.AddTransient<MobileAuthClient>(c => new MobileAuthClient(
            c.GetRequiredService<ClientAppSettings>(),
            c.GetRequiredService<BaseUrlProvider>(),
            c.GetRequiredService<ILogger<MobileAuthClient>>()));

        // UI
        services.AddSingleton<NavigationInterceptor>(c => new NavigationInterceptor(c));
        services.AddTransient<MainPage>();

#if ANDROID
        services.AddTransient<Notification.UI.Blazor.IDeviceTokenRetriever>(c => new AndroidDeviceTokenRetriever(c));
        services.AddScoped<IAudioOutputController>(c => new AndroidAudioOutputController(c));
        services.AddScoped<ClipboardUI>(c => new AndroidClipboardUI(
            c.GetRequiredService<IJSRuntime>()));
#elif IOS
        services.AddTransient<Notification.UI.Blazor.IDeviceTokenRetriever, IOSDeviceTokenRetriever>(_ => new IOSDeviceTokenRetriever());
#elif MACCATALYST
        services.AddTransient<Notification.UI.Blazor.IDeviceTokenRetriever, MacDeviceTokenRetriever>(_ => new MacDeviceTokenRetriever());
#elif WINDOWS
        services.AddTransient<Notification.UI.Blazor.IDeviceTokenRetriever>(_ => new WindowsDeviceTokenRetriever());
#endif

        ActualChat.UI.Blazor.JSObjectReferenceExt.TestIfIsDisconnected = JSObjectReferenceDisconnectHelper.TestIfIsDisconnected;
        // Misc.
        services.AddScoped<DisposeTracer>(c => new DisposeTracer(c));
    }

    private static Task<Symbol> GetSessionId()
        => BackgroundTask.Run(async () => {
            var step = _trace.TrackStep("Getting session id");
            Symbol sessionId = Symbol.Empty;
            const string sessionIdStorageKey = "Fusion.SessionId";
            var storage = SecureStorage.Default;
            try {
                var storedSessionId = await storage.GetAsync(sessionIdStorageKey).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(storedSessionId))
                    sessionId = storedSessionId;
            }
 #pragma warning disable RCS1075
            catch (Exception) {
 #pragma warning restore RCS1075
                // ignored
                // https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
                // TODO: configure selective backup, to prevent app crashes after re-installing
                // https://learn.microsoft.com/en-us/xamarin/essentials/secure-storage?tabs=android#selective-backup
            }
            if (sessionId.IsEmpty) {
                sessionId = new SessionFactory().CreateSession().Id;
                try {
                    await storage.SetAsync(sessionIdStorageKey, sessionId.Value).ConfigureAwait(false);
                }
 #pragma warning disable RCS1075
                catch (Exception) {
 #pragma warning restore RCS1075
                    // ignored
                }
            }
            step.Complete();
            return sessionId;
        });
}
