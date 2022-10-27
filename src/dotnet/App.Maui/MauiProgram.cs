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

namespace ActualChat.App.Maui;

 #pragma warning disable VSTHRD002

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            })
            .ConfigureLifecycleEvents(ConfigureLifecycleEvents);

        var services = builder.Services;
        services.AddMauiBlazorWebView();

#if DEBUG || DEBUG_MAUI
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        services.AddLogging(logging => logging
            .AddDebug()
            .SetMinimumLevel(LogLevel.Information)
        );

        services.TryAddSingleton(builder.Configuration);

        var sessionId = GetSessionId();
        var settings = new ClientAppSettings { SessionId = sessionId };
        services.TryAddSingleton(settings);

#if IS_FIXED_ENVIRONMENT_PPRODUCTION
        var environment = Environments.Production;
#else
        var environment = Environments.Development;
#endif

        services.AddSingleton(c => new HostInfo {
            AppKind = AppKind.Maui,
            Environment = environment,
            Configuration = c.GetRequiredService<IConfiguration>(),
            BaseUrl = GetBaseUrl(),
        });

        builder.ConfigureMauiHandlers(handlers => {
            handlers.AddHandler<IBlazorWebView, MauiBlazorWebViewHandler>();
        });

        ConfigureServices(services);

        var mauiApp = builder.Build();

        AppServices = mauiApp.Services;

        Constants.HostInfo = AppServices.GetRequiredService<HostInfo>();
        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = AppServices.LogFor(typeof(WebMReader));

        // MAUI does not start HostedServices, so we do this manually.
        // https://github.com/dotnet/maui/issues/2244
        StartHostedServices(mauiApp);

        return mauiApp;
    }

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
                // Sometimes I observe a situation that BlazorWebView contains 2 history items and can go back,
                // but it doesn't handle BackPressed event.
                // This handler forces BackPressed handling on BlazorWebView and
                // prevents the situation that app is closed on BackPressed
                // while BlazorWebView still can go back.
                if (Application.Current?.MainPage is MainPage mainPage) {
                    var webView = mainPage.PlatformWebView;
                    if (webView != null) {
                        if (webView.CanGoBack()) {
                            webView.GoBack();
                            return true;
                        }
                        Android.Util.Log.Debug(AndroidConstants.LogTag, $"MauiProgram.OnBackPressed. Can not go back. Current url is '{webView.Url}'");
                    }
                }
                // Move app to background as Home button acts.
                // It prevents scenario when app is running, but activity is destroyed.
                activity.MoveTaskToBack(true);
                return true;
            });
        });
#endif
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // HttpClient
        services.RemoveAll<IHttpClientFactory>();
        services.AddSingleton<IHttpClientFactory, NativeHttpClientFactory>();

        AppConfigurator.ConfigureServices(services, typeof(Module.BlazorUIClientAppModule)).Wait();

        // Auth
        services.AddScoped<IClientAuth, MauiClientAuth>();
        services.AddTransient<MobileAuthClient>();

        // UI
        services.AddSingleton<NavigationInterceptor>();
        services.AddTransient<MainPage>();

        //Firebase messaging
#if ANDROID
        services.AddTransient<Notification.UI.Blazor.IDeviceTokenRetriever, AndroidDeviceTokenRetriever>();
#endif

        // Misc.
        services.AddScoped<DisposeTracer>();
    }

    private static Symbol GetSessionId()
        => BackgroundTask.Run(async () => {
            Symbol sessionId = Symbol.Empty;
            const string sessionIdStorageKey = "Fusion.SessionId";
            var storage = SecureStorage.Default;
            var storedSessionId = await storage.GetAsync(sessionIdStorageKey).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(storedSessionId))
                sessionId = storedSessionId;
            if (sessionId.IsEmpty) {
                sessionId = new SessionFactory().CreateSession().Id;
                await storage.SetAsync(sessionIdStorageKey, sessionId.Value).ConfigureAwait(false);
            }
            return sessionId;
        }).Result;
}
