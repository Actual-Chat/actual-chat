using ActualChat.Hosting;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Configuration;
using ActualChat.UI.Blazor.App;
using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.LifecycleEvents;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;

namespace ActualChat.App.Maui;

 #pragma warning disable VSTHRD002

public static partial class MauiProgram
{
    private static readonly Tracer Tracer = MauiDiagnostics.Tracer[nameof(MauiProgram)];
    private static HostInfo HostInfo => Constants.HostInfo;

    public static partial LoggerConfiguration ConfigurePlatformLogger(LoggerConfiguration loggerConfiguration);

    public static partial string? GetAppSettingsFilePath();

    public static MauiApp CreateMauiApp()
    {
        using var _1 = Tracer.Region();

        FusionSettings.Mode = FusionMode.Client;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        MauiThreadPoolSettings.Apply();

#if WINDOWS
        if (Tracer.IsEnabled) {
            // EventSources and EventListeners do not work in Mono. So no sense to enable but platforms different from Windows
            // MauiBlazorOptimizer.EnableDependencyInjectionEventListener();
        }
#endif

        try {
            const string baseUrl = "https://" + MauiConstants.Host + "/";
            AppSettings = new MauiAppSettings(baseUrl);
            _ = MauiSessionResolver.Start();

            var appBuilder = MauiApp.CreateBuilder().UseMauiApp<App>();
            Constants.HostInfo = CreateHostInfo(appBuilder.Configuration);
#if true
            // Normal start
            ConfigureApp(appBuilder, false);
            var app = appBuilder.Build();
            AppServicesReady(app);
            return app;
#else
            // Lazy start
            var earlyApp = ConfigureApp(appBuilder, true).Build();
            var whenAppServicesReady = Task.Run(() => CreateLazyAppServices(earlyApp.Services));
            var appServices = new CompositeServiceProvider(
                earlyApp.Services,
                whenAppServicesReady,
                CreateLazyServiceFilter(),
                earlyApp);
            AppServicesReady(appServices);
            return (MauiApp)typeof(MauiApp)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .First()
                .Invoke(new object[] { appServices });
#endif
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Failed to build MAUI app");
            throw;
        }
    }

    private static MauiAppBuilder ConfigureApp(MauiAppBuilder builder, bool isEarlyApp)
    {
        using var _ = Tracer.Region();

        builder = builder
            .ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            })
            .ConfigureLifecycleEvents(ConfigurePlatformLifecycleEvents)
            .UseAppLinks();

        var services = builder.Services;

        // Core services
        services.AddSingleton(AppSettings);
        services.AddSingleton(HostInfo);
        services.AddSingleton(HostInfo.Configuration);
        services.AddMauiDiagnostics(true);

        // Core MAUI services
        services.AddMauiBlazorWebView();
#if DEBUG
        // Temporarily allow developer tools for all configurations
        services.AddBlazorWebViewDeveloperTools();
#endif

        services.AddTransient(_ => new MainPage(new MauiNavigationInterceptor()));
        builder.ConfigureMauiHandlers(handlers => {
            handlers.AddHandler<IBlazorWebView, MauiBlazorWebViewHandler>();
        });

        if (!isEarlyApp)
            ConfigureAppServices(services, null);

        return builder;
    }

    private static Task<IServiceProvider> CreateLazyAppServices(IServiceProvider earlyServices)
    {
        using var _1 = Tracer.Region();
        var services = new ServiceCollection();
        ConfigureAppServices(services, earlyServices);
        var appServices = services.BuildServiceProvider();
        return Task.FromResult((IServiceProvider)appServices);
    }

    private static void AppServicesReady(MauiApp app)
    {
        AppServices = app.Services;
        LoadingUI.MarkAppBuilt();
        _ = Task.Run(async () => {
            var sessionResolver = AppServices.GetRequiredService<ISessionResolver>();
            if (sessionResolver is MauiSessionResolver mauiSessionResolver)
                _ = mauiSessionResolver.AcquireSession();

            var session = await sessionResolver.SessionTask.ConfigureAwait(false);
            var appServiceStarter = AppServices.GetRequiredService<AppServiceStarter>();
            _ = appServiceStarter.PostSessionWarmup(session, CancellationToken.None);
        });
    }

    // ConfigureXxx

    private static void ConfigureAppServices(IServiceCollection services, IServiceProvider? earlyServices)
    {
        using var _ = Tracer.Region();

        // Singleton services visible from lazy services
        services.AddSingleton(AppSettings);
        services.AddSingleton(HostInfo);
        services.AddSingleton(HostInfo.Configuration);
        services.AddMauiDiagnostics(false);

#if IOS
        // HTTP client
        services.RemoveAll<IHttpClientFactory>();
        services.AddSingleton(c => new NativeHttpClientFactory(c));
        services.AddSingleton<IHttpClientFactory>(c => c.GetRequiredService<NativeHttpClientFactory>());
        services.AddSingleton<IHttpMessageHandlerFactory>(c => c.GetRequiredService<NativeHttpClientFactory>());
#endif

        // Other non-lazy services visible from lazy services
        if (earlyServices != null)
            ConfigureNonLazyServicesVisibleFromLazyServices(services);

        // All other (module) services
        AppStartup.ConfigureServices(services, AppKind.MauiApp, c => new HostModule[] {
            new Module.MauiAppModule(c),
        });

        // Platform services
        services.AddPlatformServices();
    }

    private static void ConfigureNonLazyServicesVisibleFromLazyServices(IServiceCollection services)
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

    // CreateXxx

    public static HostInfo CreateHostInfo(IConfiguration configuration)
    {
        // Add HostInfo
        var platform = DeviceInfo.Current.Platform;
        var clientKind = ClientKind.Unknown;
        if (platform == DevicePlatform.Android)
            clientKind = ClientKind.Android;
        else if (platform == DevicePlatform.iOS)
            clientKind = ClientKind.Ios;
        else if (platform == DevicePlatform.WinUI)
            clientKind = ClientKind.Windows;
        else if (platform == DevicePlatform.macOS)
            clientKind = ClientKind.MacOS;

#if IS_FIXED_ENVIRONMENT_PRODUCTION || !DEBUG
        var environment = Environments.Production;
#else
        var environment = Environments.Development;
#endif

        var deviceInfo = DeviceInfo.Current;
        var hostInfo = new HostInfo {
            AppKind = AppKind.MauiApp,
            ClientKind = clientKind,
            Environment = environment,
            Configuration = configuration,
            BaseUrl = AppSettings.BaseUrl,
            DeviceModel = deviceInfo.Model,
        };
        return hostInfo;
    }

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

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => Log.Information("Unhandled exception, isTerminating={IsTerminating}. \n{Exception}",
            e.IsTerminating,
            e.ExceptionObject);
}
