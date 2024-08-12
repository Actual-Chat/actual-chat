using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Configuration;
using ActualChat.UI.Blazor.App;
using ActualChat.App.Maui.Services;
using ActualChat.Module;
using ActualChat.Security;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.LifecycleEvents;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Diagnostics;
using banditoth.MAUI.DeviceId;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using Sentry;
using Sentry.Maui.Internal;
using Serilog;
using ActualLab.Rpc;
using ILogger = Microsoft.Extensions.Logging.ILogger;
#if IOS
using Foundation;
#endif

namespace ActualChat.App.Maui;

#pragma warning disable VSTHRD002, IL2026

public static partial class MauiProgram
{
    private static ILogger? _log;

    private static HostInfo HostInfo => Constants.HostInfo;
    private static readonly Tracer Tracer = MauiDiagnostics.Tracer[nameof(MauiProgram)];
    private static ILogger Log => _log ??= StaticLog.For(typeof(MauiProgram));

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiDiagnostics))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiProgram))]
    public static MauiApp CreateMauiApp()
    {
        using var _1 = Tracer.Region();

        RpcDefaults.Mode = RpcMode.Client;
        FusionDefaults.Mode = FusionMode.Client;
        RpcCallTimeouts.Defaults.Command = new RpcCallTimeouts(20, null); // 20s for connect

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        MauiThreadPoolSettings.Apply();
#if IOS
        NSHttpCookieStorage.SharedStorage.AcceptPolicy = NSHttpCookieAcceptPolicy.Always;
#endif
#if ANDROID || WINDOWS
        _ = Task.Run(() => new SentryOptions()); // JIT compile SentryOptions in advance
#endif
        AppUIOtelSetup.SetupConditionalPropagator();
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
            // NOTE: It's enabled in Debug mode only hence there is no performance penalties in Release mode.
            EnableContainerValidation(appBuilder);
#endif
            var environment =
#if IS_PRODUCTION_ENV || !DEBUG
                Environments.Production;
#else
                Environments.Development;
#endif
            Constants.HostInfo = ClientAppStartup.CreateHostInfo(
                appBuilder.Configuration,
                environment,
                DeviceInfo.Current.Model,
                HostKind.MauiApp,
                MauiSettings.AppKind,
                MauiSettings.BaseUrl);
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
            Log.LogCritical(ex, "Failed to build MAUI app");
            throw;
        }
    }

    private static void ConfigureApp(MauiAppBuilder builder, bool isEarlyApp)
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
        services.AddScoped<Mutable<MauiWebView?>>();
// #if DEBUG
        services.AddBlazorWebViewDeveloperTools();
// #endif

        services.AddTransient(_ => new MainPage());
        if (!isEarlyApp)
            ConfigureAppServices(services, null);
    }

    private static void AddSafeJSRuntime(IServiceCollection services)
    {
        var jsRuntimeRegistration = services.FirstOrDefault(c => c.ServiceType == typeof(IJSRuntime));
        if (jsRuntimeRegistration == null) {
            Log.LogWarning("IJSRuntime registration is not found. Can't override WebViewJSRuntime");
            return;
        }
        var webViewJSRuntimeType = jsRuntimeRegistration.ImplementationType;
        if (webViewJSRuntimeType == null) {
            Log.LogWarning("IJSRuntime registration has no ImplementationType. Can't override WebViewJSRuntime");
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
                if (!safeJSRuntime.IsReady && safeJSRuntime.MarkReady())
                    // The very first IJSRuntime service resolved first time from PageContext is cast to WebViewJSRuntime
                    // to being attached to WebView. So we need to return the original WebViewJSRuntime instance
                    // specifically for this call, and after that we can return SafeJSRuntime.
                    // See https://github.com/dotnet/aspnetcore/blob/410efd482f494d1ab05ce25b932b5788699c2308/src/Components/WebView/WebView/src/PageContext.cs#L44
                    return safeJSRuntime.WebViewJSRuntime;
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
        StaticLog.Factory = app.Services.LoggerFactory();
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
        MauiAppStartup.ConfigureServices(services);

        // Platform services
        services.AddPlatformServices();
    }

    private static void ConfigureNonLazyServicesVisibleFromLazyServices(IServiceCollection services)
    {
        services.AddSingleton(HostInfo);
        services.AddSingleton(HostInfo.Configuration);
        services.AddMauiDiagnostics(false);

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
        // See https://github.com/dotnet/maui/blob/main/src/Core/src/Hosting/Dispatching/AppHostBuilderExtensions.cs
        services.Replace(typeof(IDispatcher), static sd => sd.ChangeLifetime(ServiceLifetime.Singleton));
        services.ReplaceAll(typeof(IMauiInitializeScopedService), static sd => sd.ChangeLifetime(ServiceLifetime.Transient));
        // Enable validation on container
        // NOTE: will be improved later, see https://github.com/dotnet/maui/issues/18519
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
        => Log.LogInformation("Unhandled exception, isTerminating={IsTerminating}.\n{Exception}",
            e.IsTerminating,
            e.ExceptionObject);
}
