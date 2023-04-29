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

namespace ActualChat.App.Maui;

 #pragma warning disable VSTHRD002

public static partial class MauiProgram
{
    private static readonly Tracer Tracer = MauiDiagnostics.Tracer[nameof(MauiProgram)];

    public static partial LoggerConfiguration ConfigurePlatformLogger(LoggerConfiguration loggerConfiguration);

    public static MauiApp CreateMauiApp()
    {
        using var _1 = Tracer.Region(nameof(CreateMauiApp));

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
            MauiSession.RestoreOrCreate();
#if false
            // Normal start
            var appBuilder = CreateAppBuilder(false);
            Settings.WhenSessionReady.Wait();
            var app = appBuilder.Build();
            AppServices = app.Services;
            LoadingUI.MarkMauiAppBuilt(Tracer.Elapsed);
            return app;
#else
            // Lazy start
            var earlyApp = CreateAppBuilder(true).Build();
            var whenAppServicesReady = Task.Run(() => CreateLazyAppServices(earlyApp.Services));
            var appServices = new CompositeServiceProvider(
                earlyApp.Services,
                whenAppServicesReady,
                CreateLazyServiceFilter(),
                earlyApp);
            AppServices = appServices;
            LoadingUI.MarkMauiAppBuilt(Tracer.Elapsed);
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

    private static MauiAppBuilder CreateAppBuilder(bool isLazy)
    {
        using var _ = Tracer.Region(nameof(CreateAppBuilder));

        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        if (Constants.Sentry.EnabledFor.Contains(AppKind.MauiApp))
            builder = builder.UseSentry(options => options.ConfigureForApp());

        builder = builder
            .ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            })
            .ConfigureLifecycleEvents(ConfigurePlatformLifecycleEvents)
            .UseAppLinks();

        // Core services
        var services = builder.Services;
        services.AddSingleton(AppSettings);
        services.AddSingleton(c => new LoadingUI(c)); // LoadingUI should be available early here, and as singleton
        services.AddMauiDiagnostics(true);

        // Core MAUI services
        services.AddMauiBlazorWebView();
#if true || DEBUG
        // Temporarily allow developer tools for all configurations
        services.AddBlazorWebViewDeveloperTools();
#endif

        services.AddTransient(_ => new MainPage(new MauiNavigationInterceptor()));
        builder.ConfigureMauiHandlers(handlers => {
            handlers.AddHandler<IBlazorWebView, MauiBlazorWebViewHandler>();
        });

        if (!isLazy)
            ConfigureAppServices(services, builder.Configuration, null);

        return builder;
    }

    private static async Task<IServiceProvider> CreateLazyAppServices(IServiceProvider earlyServices)
    {
        using var _1 = Tracer.Region(nameof(CreateLazyAppServices));

        var services = new ServiceCollection();
        var configuration = earlyServices.GetRequiredService<IConfiguration>();
        ConfigureAppServices(services, configuration, earlyServices);
        var appServices = services.BuildServiceProvider();

        var appServiceStarter = appServices.GetRequiredService<AppServiceStarter>();
        _ = appServiceStarter.PreSessionWarmup(CancellationToken.None);
        await AppSettings.WhenSessionReady.ConfigureAwait(false);

        return appServices;
    }

    private static void ConfigureAppServices(IServiceCollection services, IConfiguration configuration, IServiceProvider? earlyServices)
    {
        using var _ = Tracer.Region(nameof(ConfigureAppServices));

        services.AddSingleton(AppSettings);
        services.AddSingleton(configuration);

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

        var hostInfo = new HostInfo {
            AppKind = AppKind.MauiApp,
            ClientKind = clientKind,
            Environment = environment,
            Configuration = configuration,
            BaseUrl = AppSettings.BaseUrl,
            Platform = PlatformInfoProvider.GetPlatform(),
        };
        Constants.HostInfo = hostInfo;
        services.AddSingleton(_ => hostInfo);

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

        // Non-lazy services visible from lazy services
        if (earlyServices != null) {
            var loadingUI = earlyServices.GetRequiredService<LoadingUI>();
            services.AddSingleton(loadingUI);
            services.AddMauiDiagnostics(false);
            ConfigureNonLazyServicesVisibleFromLazyServices(services);
        }

        // Auth
        services.AddScoped<IClientAuth>(c => new MauiClientAuth(c));
        services.AddSingleton(c => new BaseUrlProvider(c.GetRequiredService<UrlMapper>().BaseUrl));
        services.AddTransient(c => new MobileAuthClient(
            c.GetRequiredService<HttpClient>(),
            c.GetRequiredService<ILogger<MobileAuthClient>>()));

        // UI
        services.AddScoped<BrowserInfo>(c => new MauiBrowserInfo(c));
        services.AddScoped<KeepAwakeUI>(c => new MauiKeepAwakeUI(c));

        // Misc.
        JSObjectReferenceExt.TestIfDisconnected = JSObjectReferenceDisconnectHelper.TestIfIsDisconnected;
        services.AddScoped<DisposeTracer>(c => new DisposeTracer(c));

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
