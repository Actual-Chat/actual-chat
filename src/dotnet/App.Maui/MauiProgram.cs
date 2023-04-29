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
    private static Tracer _tracer = null!;
    private static ClientAppSettings _settings = null!;

    public static partial LoggerConfiguration ConfigurePlatformLogger(LoggerConfiguration loggerConfiguration);

    public static MauiApp CreateMauiApp()
    {
        _tracer = MauiDiagnostics.Tracer[nameof(MauiProgram)];
        using var _1 = _tracer.Region(nameof(CreateMauiApp));

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        MauiThreadPoolSettings.Apply();

#if WINDOWS
        if (_tracer.IsEnabled) {
            // EventSources and EventListeners do not work in Mono. So no sense to enable but platforms different from Windows
            // MauiBlazorOptimizer.EnableDependencyInjectionEventListener();
        }
#endif

        try {
            const string baseUrl = "https://" + MauiConstants.Host + "/";
            _settings = new ClientAppSettings(baseUrl);
            MauiSession.RestoreOrCreate(_settings);
#if false
            // Normal start
            var appBuilder = CreateAppBuilder(false);
            _settings.WhenSessionReady.Wait();
            var app = appBuilder.Build();
            AppServices = app.Services;
            LoadingUI.MarkMauiAppBuilt(_tracer.Elapsed);
            return app;
#else
            // Lazy start
            var miniApp = CreateAppBuilder(true).Build();
            var whenAppServicesReady = Task.Run(() => CreateLazyAppServices(miniApp.Configuration));
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
#endif
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Failed to build MAUI app");
            throw;
        }
    }

    private static MauiAppBuilder CreateAppBuilder(bool isLazy)
    {
        using var _ = _tracer.Region(nameof(CreateAppBuilder));

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
        services.AddSingleton(_settings);
        services.AddMauiDiagnostics(true);
        services.AddMauiBlazorWebView();

#if true || DEBUG
        // Temporarily allow developer tools for all configurations
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        services.AddTransient(_ => new MainPage(new NavigationInterceptor(_settings)));
        builder.ConfigureMauiHandlers(handlers => {
            handlers.AddHandler<IBlazorWebView, MauiBlazorWebViewHandler>();
        });

        if (!isLazy)
            ConfigureAppServices(services, builder.Configuration, false);

        return builder;
    }

    private static async Task<IServiceProvider> CreateLazyAppServices(IConfiguration configuration)
    {
        using var _1 = _tracer.Region(nameof(CreateLazyAppServices));

        var services = new ServiceCollection();
        ConfigureAppServices(services, configuration, true);
        var appServices = services.BuildServiceProvider();

        var appServiceStarter = appServices.GetRequiredService<AppServiceStarter>();
        _ = appServiceStarter.PreSessionWarmup(CancellationToken.None);
        await _settings.WhenSessionReady.ConfigureAwait(false);

        return appServices;
    }

    private static void ConfigureAppServices(IServiceCollection services, IConfiguration configuration, bool isLazy)
    {
        using var _ = _tracer.Region(nameof(ConfigureAppServices));

        if (isLazy) {
            services.AddSingleton(_settings);
            services.AddSingleton(configuration);
            services.AddMauiDiagnostics(false);
            ConfigureNonLazyServicesVisibleFromLazyServices(services);
        }

        services.AddSingleton(new ScopedTracerProvider(_tracer)); // We don't want to have scoped tracers in MAUI app

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
            BaseUrl = _settings.BaseUrl,
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

        // Auth
        services.AddScoped<IClientAuth>(c => new MauiClientAuth(c));
        services.AddSingleton<BaseUrlProvider>(c => new BaseUrlProvider(
            c.GetRequiredService<UrlMapper>().BaseUrl));
        services.AddTransient<MobileAuthClient>(c => new MobileAuthClient(
            c.GetRequiredService<ClientAppSettings>(),
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
