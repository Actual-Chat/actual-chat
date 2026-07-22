using ActualChat.App.Maui.Module;
using ActualChat.App.Maui.Services;
using ActualChat.Hosting;
using ActualChat.Logging;
using ActualChat.Maui.Module;
using ActualChat.Module;
using ActualChat.Security;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Services;
using ActualChat.UI.Diagnostics;
using banditoth.MAUI.DeviceId;
using banditoth.MAUI.DeviceId.Interfaces;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.JSInterop;
using Microsoft.Maui.LifecycleEvents;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Tracer = ActualChat.Performance.Tracer;
#if IOS
using Foundation;
#endif

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static HostInfo HostInfo => Constants.HostInfo;
    private static ILogger Log => field ??= StaticLog.For(typeof(MauiProgram));
    private static Tracer Tracer => field ??= Tracer.Default[nameof(MauiProgram)];

    static MauiProgram()
    {
        // To enable early file-based crash logging for Native AOT bring-up, uncomment MauiNativeAotLogging.Use().
        // Normally it should be kept off, see MauiNativeAotLogging for caveats.
        // MauiNativeAotLogging.Use();
        MauiDiagnostics.Initialize();
    }

    public static MauiApp CreateMauiApp()
    {
        using var _1 = Tracer.MethodRegion();
        MauiStartupBreadcrumbs.Add("CreateMauiApp");

        // Parse -t <seconds> for auto-shutdown (used for AOT testing)
        var args = Environment.GetCommandLineArgs();
        for (var i = 1; i < args.Length; i++) {
            if (args[i] == "-t" && i + 1 < args.Length && int.TryParse(args[i + 1], out var seconds)) {
                _ = Task.Run(async () => {
                    await Task.Delay(seconds * 1000).ConfigureAwait(false);
                    Log.LogInformation("Auto-shutdown after {Seconds}s", seconds);
                    Environment.Exit(0);
                });
                i++;
            }
        }

        MauiExceptionHandlers.Use();
        MauiRuntimeSettings.Apply();
#if ANDROID
        ActivateDataCollectionIfEnabled(Android.App.Application.Context);
        AndroidMainThreadMonitor.Activate();
#endif
        ClientStartup.Initialize();
        // MainThreadTracker.Activate();
#if DEBUG
        // NOTE: Keep the noise down.
        // It might be activated in Debug mode only hence no sense to keep this code in Release mode.
        if (FirstChanceExceptionLogger.IsActivated)
            FirstChanceExceptionLogger.ShouldSkip += ShouldSkipFce;
#endif

        FixStaticContentProvider();
#if IOS
        NSHttpCookieStorage.SharedStorage.AcceptPolicy = NSHttpCookieAcceptPolicy.Always;
#endif
        AppUIOtelSetup.SetupConditionalPropagator();
#if WINDOWS
        if (Tracer.IsEnabled) {
            // EventSources and EventListeners do not work in Mono. So no sense to enable but platforms different from Windows
            // MauiBlazorOptimizer.EnableDependencyInjectionEventListener();
        }
#endif

        try {
            // Maui app plays a host role for a blazor app running in a web view.
            MauiAppBuilder? appBuilder;
            using (Tracer.Region($"{nameof(MauiApp)}.{nameof(MauiApp.CreateBuilder)}")) {
                appBuilder = MauiApp.CreateBuilder();
                Constants.HostInfo = CreateHostInfo(appBuilder.Configuration);
                ConfigureMauiApp(appBuilder);
            }
            MauiStartupBreadcrumbs.Add("MauiApp configured");
#if DEBUG
            // NOTE: It's enabled in Debug mode only hence there are no performance penalties in Release mode.
            EnableContainerValidation(appBuilder);
#endif
            var app = appBuilder.Build();
            MauiStartupBreadcrumbs.Add("MauiApp built");
            StaticLog.Factory = app.Services.LoggerFactory();

            AppNonScopedServiceStarter.WarmupStaticServices(HostInfo);
            MauiStartupBreadcrumbs.Add("static services warmed up");

#pragma warning disable CA2025
            BlazorWebViewApp.Initialize(() => BuildBlazorViewAppInternal(app));
#pragma warning restore CA2025

            SetupBlazorViewAppPostBuildRoutine();

            LoadingUI.MarkAppBuilt();
            MauiStartupBreadcrumbs.Add("CreateMauiApp completed");

            return app;
        }
        catch (Exception ex) {
            Log.LogCritical(ex, "Failed to build MAUI app");
            throw;
        }
    }

    private static HostInfo CreateHostInfo(IConfiguration configuration)
    {
        var environment =
#if IS_PRODUCTION_ENV || !DEBUG
            Environments.Production;
#else
            Environments.Development;
#endif
        var hostInfo = ClientStartup.CreateHostInfo(
            configuration,
            environment,
            DeviceInfo.Current.Model,
            HostKind.MauiApp,
            MauiSettings.AppKind,
            MauiSettings.BaseUrl);
        return hostInfo;
    }

    private static Task<BlazorWebViewApp> BuildBlazorViewAppInternal(MauiApp app)
    {
        using var _1 = Tracer.MethodRegion();

        _ = MauiSession.Start();
        BlazorWebViewApp blazorViewApp;
        // ReSharper disable once ExplicitCallerInfoArgument
        using (Tracer.Region("RunBlazorViewAppBuilder")) {
            var blazorViewAppBuilder = BlazorWebViewApp.CreateBuilder();
            ConfigureBlazorApp(blazorViewAppBuilder);
            InjectMauiAppServices(blazorViewAppBuilder, app);
            blazorViewApp = blazorViewAppBuilder.Build();
        }
        return Task.FromResult(blazorViewApp);
    }

    private static void SetupBlazorViewAppPostBuildRoutine()
        => _ = Task.Run(BlazorViewAppPostBuildRoutine);

    private static async Task BlazorViewAppPostBuildRoutine()
    {
        var blazorViewApp = await BlazorWebViewApp.WhenAppReady.ConfigureAwait(false);
        var services = blazorViewApp.Services;
        var mauiSession = services.GetRequiredService<MauiSession>();
        _ = mauiSession.Acquire();
        var trueSessionResolver = services.GetRequiredService<TrueSessionResolver>();
        await trueSessionResolver.SessionTask.ConfigureAwait(false);
        var appRootServiceStarter = services.GetRequiredService<AppNonScopedServiceStarter>();
        _ = appRootServiceStarter.StartNonScopedServices();
    }

    private static void InjectMauiAppServices(BlazorWebViewAppBuilder blazorViewAppBuilder, MauiApp app)
    {
        var c = app.Services;
        var services = blazorViewAppBuilder.Services;
        services.Replace(ServiceDescriptor.Singleton(c.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton(c.GetRequiredService<TailLoggerSinkSet>());
        var dispatcher = c.GetRequiredService<IDispatcher>();
        services.AddSingleton(dispatcher);
    }

    private static void FixStaticContentProvider()
    {
#if WINDOWS
        var staticContentProviderType = Type.GetType(
            "Microsoft.AspNetCore.Components.WebView.Maui.StaticContentProvider, Microsoft.AspNetCore.Components.WebView.Maui");
        if (staticContentProviderType == null)
            throw StandardError.Constraint("Static content provider not found.");

        var contentTypeProviderFieldInfo = staticContentProviderType.GetField("ContentTypeProvider", BindingFlags.Static | BindingFlags.NonPublic);
        if (contentTypeProviderFieldInfo == null)
            throw StandardError.Constraint("Static content provider does not have a 'ContentTypeProvider' field.");

        var contentTypeProviderType = contentTypeProviderFieldInfo.FieldType;
        var contentTypeProvider = contentTypeProviderFieldInfo.GetValue(null);
        if (contentTypeProvider == null)
            throw StandardError.Constraint("'ContentTypeProvider' field has null value.");

        var mappingsPropertyInfo = contentTypeProviderType.GetProperty("Mappings", BindingFlags.Instance | BindingFlags.Public);
        var mapping = (IDictionary<string,string>)mappingsPropertyInfo!.GetValue(contentTypeProvider)!;
        mapping[".mjs"] = "text/javascript";
#else
        // StaticContentProvider works fine on non-Windows platforms
#endif
    }

    private static void ConfigureMauiApp(MauiAppBuilder builder)
    {
        using var _ = Tracer.MethodRegion();

        builder = builder
            .UseMauiBlazorApp<App>()
            .ConfigureMauiHandlers(static handlers
                => handlers.AddHandler<IBlazorWebView>(_ => new CustomBlazorWebViewHandler()))
            .ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            })
            .ConfigureLifecycleEvents(ConfigurePlatformLifecycleEvents);

        var services = builder.Services;

        // Core services
        services.AddSingleton(HostInfo);
        services.AddSingleton(HostInfo.Configuration);
        services.AddMauiDiagnostics(true);
    }

    private static void ConfigureBlazorApp(BlazorWebViewAppBuilder builder)
    {
        using var _ = Tracer.MethodRegion();
        var services = builder.Services;
        // Core services
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(Tracer.Default);
        services.Add(GetDeviceIdProviderServiceDescriptor());
        // Core MAUI services
        services.AddMauiBlazorWebView();
        AddSafeJSRuntime(services);
        if (MauiSettings.AreDevToolsEnabled)
            services.AddBlazorWebViewDeveloperTools();
        ConfigureBlazorWebViewAppServices(services);
    }

    private static ServiceDescriptor GetDeviceIdProviderServiceDescriptor()
        => MauiApp.CreateBuilder(false)
            .ConfigureDeviceIdProvider()
            .Services.First(c => c.ServiceType == typeof(IDeviceIdProvider));

    private static void AddSafeJSRuntime(IServiceCollection services)
    {
        var jsRuntimeRegistration = services.FirstOrDefault(c => c.ServiceType == typeof(IJSRuntime));
        if (jsRuntimeRegistration == null) {
            Log.LogWarning("Can't add SafeJSRuntime: IJSRuntime registration is not found");
            return;
        }
        var webViewJSRuntimeType = jsRuntimeRegistration.ImplementationType;
        if (webViewJSRuntimeType == null) {
            Log.LogWarning("Can't add SafeJSRuntime: IJSRuntime registration has no ImplementationType");
            return;
        }
        services.Remove(jsRuntimeRegistration);
        services.Add(new ServiceDescriptor(
            typeof(SafeJSRuntime),
            c => {
                var wrapped = (IJSRuntime)ActivatorUtilities.CreateInstance(c, webViewJSRuntimeType);
                wrapped.InjectJsonTypeInfoResolvers();
                return new SafeJSRuntime(wrapped);
            },
            jsRuntimeRegistration.Lifetime));
        services.Add(new ServiceDescriptor(
            typeof(IJSRuntime),
            c => {
                var safeJSRuntime = c.GetRequiredService<SafeJSRuntime>();
                if (!safeJSRuntime.IsReady && safeJSRuntime.MarkReady())
                    // The very first IJSRuntime service resolved first time from PageContext is cast to WrappedJSRuntime
                    // to being attached to WebView. So we need to return the original WrappedJSRuntime instance
                    // specifically for this call, and after that we can return SafeJSRuntime.
                    // See https://github.com/dotnet/aspnetcore/blob/410efd482f494d1ab05ce25b932b5788699c2308/src/Components/WebView/WebView/src/PageContext.cs#L44
                    return safeJSRuntime.WrappedJSRuntime;

                // After that there is no more bindings with implementation type, so we can return protected JSRuntime.
                return safeJSRuntime;
            },
            ServiceLifetime.Transient));
    }

    // ConfigureXxx

    private static void ConfigureBlazorWebViewAppServices(IServiceCollection services)
    {
        using var _ = Tracer.MethodRegion();

        if (MauiHttpClientFactory.IsEnabled) {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton(c => new MauiHttpClientFactory(c));
            services.AddSingleton<IHttpClientFactory>(c => c.GetRequiredService<MauiHttpClientFactory>());
            services.AddSingleton<IHttpMessageHandlerFactory>(c => c.GetRequiredService<MauiHttpClientFactory>());
        }

        // All other (module) services
        ClientStartup.ConfigureServices(services, Constants.HostInfo, c => [
            new MauiModule(c),
            new MauiAppModule(c)]);

        // Platform services
        services.ConfigureBlazorWebViewAppPlatformServices();
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

    private static bool ShouldSkipFce(Exception e)
    {
        if (e is PlatformNotSupportedException) {
            if (e.StackTrace is not null
                && e.StackTrace.Contains("OpenTelemetry.Resources.ResourceBuilder..cctor"))
                return true;
        }
        else if (e is TimeoutException) {
            if (e.Message == "Timeout while waiting for RPC keep-alive.")
                return true;
        }
        return false;
    }
#endif

    private static partial void ConfigureBlazorWebViewAppPlatformServices(this IServiceCollection services);
    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events);
}
