using ActualChat.Hosting;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Configuration;
using ActualChat.UI.Blazor.App;
using ActualChat.App.Maui.Services;
using ActualChat.Security;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.LifecycleEvents;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Diagnostics;
using banditoth.MAUI.DeviceId;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using Sentry;
using Serilog;
using Stl.CommandR.Rpc;

namespace ActualChat.App.Maui;

 #pragma warning disable VSTHRD002

public static partial class MauiProgram
{
    private static readonly Tracer Tracer = MauiDiagnostics.Tracer[nameof(MauiProgram)];
    private static HostInfo HostInfo => Constants.HostInfo;

    public static MauiApp CreateMauiApp()
    {
        using var _1 = Tracer.Region();

        FusionSettings.Mode = FusionMode.Client;
        RpcOutboundCommandCallMiddleware.DefaultTimeout = TimeSpan.FromSeconds(20);
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        MauiThreadPoolSettings.Apply();
        if (OSInfo.IsAndroid || OSInfo.IsWindows)
            _ = Task.Run(() => new SentryOptions()); // JIT compile SentryOptions in advance
        OtelDiagnostics.SetupConditionalPropagator();

#if WINDOWS
        if (Tracer.IsEnabled) {
            // EventSources and EventListeners do not work in Mono. So no sense to enable but platforms different from Windows
            // MauiBlazorOptimizer.EnableDependencyInjectionEventListener();
        }
#endif

        try {
            _ = MauiSession.Start();

            var appBuilder = MauiApp.CreateBuilder().UseMauiApp<App>();
#if DEBUG
            EnableContainerValidation(appBuilder);
#endif
            Constants.HostInfo = CreateHostInfo(appBuilder.Configuration);
            AppNonScopedServiceStarter.WarmupStaticServices(HostInfo);
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
            var app = (MauiApp)typeof(MauiApp)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .First()
                .Invoke(new object[] { appServices });
            AppServicesReady(app);
            return app;
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
            .ConfigureDeviceIdProvider()
            .ConfigureLifecycleEvents(ConfigurePlatformLifecycleEvents)
            .UseAppLinks();

        var services = builder.Services;

        // Core services
        services.AddSingleton(HostInfo);
        services.AddSingleton(HostInfo.Configuration);
        services.AddMauiDiagnostics(true);

        // Core MAUI services
        services.AddMauiBlazorWebView();
        AddSafeJSRuntime(services);
// #if DEBUG
        services.AddBlazorWebViewDeveloperTools();
// #endif

        services.AddTransient(_ => new MainPage());
        builder.ConfigureMauiHandlers(handlers => {
            handlers.AddHandler<IBlazorWebView, MauiBlazorWebViewHandler>();
        });

        if (!isEarlyApp)
            ConfigureAppServices(services, null);

        return builder;
    }

    private static void AddSafeJSRuntime(IServiceCollection services)
    {
        var jsRuntimeRegistration = services.FirstOrDefault(c => c.ServiceType == typeof(IJSRuntime));
        if (jsRuntimeRegistration == null) {
            DefaultLog.LogWarning("IJSRuntime registration is not found. Can't override WebViewJSRuntime");
            return;
        }
        var webViewJSRuntimeType = jsRuntimeRegistration.ImplementationType;
        if (webViewJSRuntimeType == null) {
            DefaultLog.LogWarning("IJSRuntime registration has no ImplementationType. Can't override WebViewJSRuntime");
            return;
        }
        services.Remove(jsRuntimeRegistration);
        services.Add(new ServiceDescriptor(
            typeof(SafeJSRuntime),
            c => new SafeJSRuntime((IJSRuntime)ActivatorUtilities.CreateInstance(c, webViewJSRuntimeType)),
            jsRuntimeRegistration.Lifetime));
        services.Add(new ServiceDescriptor(
            typeof(IJSRuntime),
            c => {
                var safeJSRuntime = c.GetRequiredService<SafeJSRuntime>();
                if (!safeJSRuntime.IsReady) {
                    // In MAUI Hybrid Blazor IJSRuntime service is resolved first time from PageContext and cast to WebViewJSRuntime,
                    // to being attached with WebView. So we need to return original WebViewJSRuntime instance.
                    // After that we can return 'safe' IJSRuntime implementation.
                    // See https://github.com/dotnet/aspnetcore/blob/410efd482f494d1ab05ce25b932b5788699c2308/src/Components/WebView/WebView/src/PageContext.cs#L44
                    if (safeJSRuntime.MarkReady())
                        return safeJSRuntime.WebViewJSRuntime;
                }
                // After that there is no more bindings with implementation type, so we can return protected JSRuntime.
                return safeJSRuntime;
            },
            ServiceLifetime.Transient));
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
            var mauiSession = AppServices.GetRequiredService<MauiSession>();
            _ = mauiSession.Acquire();
            var trueSessionResolver = AppServices.GetRequiredService<TrueSessionResolver>();
            await trueSessionResolver.SessionTask.ConfigureAwait(false);
            var appRootServiceStarter = AppServices.GetRequiredService<AppNonScopedServiceStarter>();
            _ = appRootServiceStarter.StartNonScopedServices();
        });
    }

    // ConfigureXxx

    private static void ConfigureAppServices(IServiceCollection services, IServiceProvider? earlyServices)
    {
        using var _ = Tracer.Region();

        // Singleton services visible from lazy services
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
            typeof(IJSRuntime),
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

#if IS_FIXED_ENVIRONMENT_PRODUCTION || !DEBUG
        var environment = Environments.Production;
#else
        var environment = Environments.Development;
#endif
        var hostInfo = new HostInfo {
            AppKind = AppKind.MauiApp,
            ClientKind = MauiSettings.ClientKind,
            Environment = environment,
            Configuration = configuration,
            BaseUrl = MauiSettings.BaseUrl,
            DeviceModel = DeviceInfo.Current.Model,
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

#if DEBUG
    private static void EnableContainerValidation(MauiAppBuilder appBuilder)
    {
        var services = appBuilder.Services;
        // NOTE(DF): MAUI has issues with internal services scope that causes validation errors.
        // Replace these registrations to pass validation. It should be safe for MAUI behavior.
        services.Replace(typeof(IDispatcher), static sd => sd.ChangeLifetime(ServiceLifetime.Singleton));
        services.ReplaceAll(typeof(IMauiInitializeScopedService), static sd => sd.ChangeLifetime(ServiceLifetime.Transient));
        // Enable validation on container
        appBuilder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));
    }
#endif

    private static partial void AddPlatformServices(this IServiceCollection services);
    private static partial void AddPlatformServicesToSkip(HashSet<Type> servicesToSkip);
    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events);

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => Log.Information("Unhandled exception, isTerminating={IsTerminating}. \n{Exception}",
            e.IsTerminating,
            e.ExceptionObject);
}
