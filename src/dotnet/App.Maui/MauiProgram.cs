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
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Notification.UI.Blazor;
using Microsoft.Maui.LifecycleEvents;
using ActualChat.UI.Blazor;
using Microsoft.JSInterop;
using Serilog;
using Serilog.Events;

namespace ActualChat.App.Maui;

 #pragma warning disable VSTHRD002

public static partial class MauiProgram
{
    private static ITraceSession _trace = null!;

    public static MauiApp CreateMauiApp()
    {
#if ANDROID
        Android.Util.Log.Debug(AndroidConstants.LogTag, "MauiProgram.CreateMauiApp");
#endif

        Log.Logger = CreateLoggerConfiguration().CreateLogger();

        _trace = ConfigureTraceSession();
        _trace.Track("MauiProgram. Trace and Logger are ready");

#if WINDOWS
        if (_trace.IsEnabled()) {
            // EventSources and EventListeners do not work in Mono. So no sense to enable but platforms different from Windows
            EnableDependencyInjectionEventListener();
        }
#endif

        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;

        try {
            var step = _trace.TrackStep("Building actual.chat maui app");
            var app = CreateMauiAppInternal();
            step.Complete();
            LoadingUI.ReportMauiAppBuildTime(_trace.Elapsed);
            return app;
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Failed to build actual.chat maui app");
            throw;
        }
    }

    private static LoggerConfiguration CreateLoggerConfiguration()
    {
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.Sentry(options => options.ConfigureForApp())
            .Enrich.With(new ThreadIdEnricher())
            .Enrich.FromLogContext()
            .Enrich.WithProperty(Serilog.Core.Constants.SourceContextPropertyName, "app.maui");
#if WINDOWS
        loggerConfiguration = loggerConfiguration
            .WriteTo.Debug(
                outputTemplate:"[{Timestamp:HH:mm:ss.fff} {Level:u3} ({ThreadID})] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
#elif ANDROID
        loggerConfiguration = loggerConfiguration
            .WriteTo.AndroidTaggedLog(
                AndroidConstants.LogTag,
                outputTemplate: "({ThreadID}) [{SourceContext}] {Message:l{NewLine:l}{Exception:l}");
#elif IOS
        loggerConfiguration = loggerConfiguration.WriteTo.NSLog();
#endif
        return loggerConfiguration;
    }

    private static ITraceSession ConfigureTraceSession()
    {
        var traceLogger = Log.Logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "*Trace*");
        return TraceSession.Default = TraceSession.IsTracingEnabled
            ? TraceSession.New("main").ConfigureOutput(m => traceLogger.Information(m)).Start()
            : TraceSession.Null;
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
            .ConfigureLifecycleEvents(ConfigureLifecycleEvents);

        var services = builder.Services;
        services.AddMauiBlazorWebView();

// Temporarily allow developer tools for all configurations
// #if DEBUG || DEBUG_MAUI
        builder.Services.AddBlazorWebViewDeveloperTools();
// #endif

        services.AddLogging(logging => ConfigureLogging(logging, true));

        services.TryAddSingleton(builder.Configuration);
        services.AddTraceSession(_trace);
        if (_trace.IsEnabled()) {
            // Use AddDispatcherProxy only to research purpose
            //AddDispatcherProxy(services, false);
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

        _ = WarmupFusionServices(AppServices);

        Constants.HostInfo = AppServices.GetRequiredService<HostInfo>();
        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = AppServices.LogFor(typeof(WebMReader));

        AwaitInitSessionInfoTask(initSessionInfoTask);

        // MAUI does not start HostedServices, so we do this manually.
        // https://github.com/dotnet/maui/issues/2244
        step = _trace.TrackStep("Starting host services");
        StartHostedServices(mauiApp);
        step.Complete();

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
            var step = _trace.TrackStep("Init session info");
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
            step.Complete();
        });

    private static void StartHostedServices(MauiApp mauiApp)
    {
        var start = mauiApp.Services.HostedServices()
            .Start();
        AwaitHostedServicesStart(start);
        // wait on purpose, CreateMauiApp is synchronous.
    }

    private static void AwaitHostedServicesStart(Task start)
        => start
            .GetAwaiter().GetResult();

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

#if ANDROID
        services.AddTransient<IDeviceTokenRetriever>(c => new AndroidDeviceTokenRetriever(c));
        services.AddScoped<IAudioOutputController>(c => new AndroidAudioOutputController(c));
        services.AddScoped<INotificationPermissions>(c => new AndroidNotificationPermissions());
        services.AddScoped<ClipboardUI>(c => new AndroidClipboardUI(
            c.GetRequiredService<IJSRuntime>()));
#elif IOS
        services.AddTransient<IDeviceTokenRetriever, IosDeviceTokenRetriever>(_ => new IosDeviceTokenRetriever());
        services.AddScoped<INotificationPermissions>(c => new IosNotificationPermissions());
#elif MACCATALYST
        services.AddTransient<IDeviceTokenRetriever, MacDeviceTokenRetriever>(_ => new MacDeviceTokenRetriever());
                services.AddScoped<INotificationPermissions>(c => new MacNotificationPermissions());
#elif WINDOWS
        services.AddTransient<IDeviceTokenRetriever>(_ => new WindowsDeviceTokenRetriever());
        services.AddScoped<INotificationPermissions>(c => new WindowsNotificationPermissions());
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
            Log.Information("About to read stored Session ID");
            var storage = SecureStorage.Default;
            try {
                var storedSessionId = await storage.GetAsync(sessionIdStorageKey).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(storedSessionId)) {
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
            step.Complete();
            return sessionId;
        });
}
