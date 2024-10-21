using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App;
using ActualChat.App.Maui.Services;
using ActualChat.Security;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.LifecycleEvents;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Diagnostics;
using banditoth.MAUI.DeviceId;
using banditoth.MAUI.DeviceId.Interfaces;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using OpenTelemetry.Trace;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Tracer = ActualChat.Performance.Tracer;
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

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiProgram))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiDiagnostics))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WebViewManager))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Editor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.Maui.AndroidWebKitWebViewManager", "Microsoft.AspNetCore.Components.WebView.Maui")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.IpcCommon", "Microsoft.AspNetCore.Components.WebView")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.IpcCommon.IncomingMessageType", "Microsoft.AspNetCore.Components.WebView")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.IpcCommon.OutgoingMessageType", "Microsoft.AspNetCore.Components.WebView")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.IpcSender", "Microsoft.AspNetCore.Components.WebView")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.IpcReceiver", "Microsoft.AspNetCore.Components.WebView")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.IpcReceiver", "Microsoft.AspNetCore.Components.WebView")]
#if false // Trying to fix the issue w/ WebSockets & LLVM
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Socket))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ClientWebSocket))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ManualResetValueTaskSourceCore<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "System.Net.WebSockets.WebSocketHandle", "System.Net.WebSockets")]
#endif
    public static MauiApp CreateMauiApp()
    {
        using var _1 = Tracer.Region();

#if Release
        // Enable FCE in Release to add breadcrumbs to crashlytics. It's also enabled for Debug build from ClientStartup.Initialize.
        FirstChanceExceptionLogger.Use();
#endif
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
#if ANDROID
        ActivateDataCollectionIfEnabled(Android.App.Application.Context);
#endif

        using(Tracer.Region(nameof(ClientStartup)+"." + nameof(ClientStartup.Initialize)))
            ClientStartup.Initialize();
        //MainThreadTracker.Activate();
        MauiThreadPoolSettings.Apply();

#if WINDOWS
        FixStaticContentProvider();
#endif
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
            // Maui app plays host role for a blazor app running in a web view.
            MauiAppBuilder? appBuilder;
            using (Tracer.Region($"{nameof(MauiApp)}.{nameof(MauiApp.CreateBuilder)}")) {
                appBuilder = MauiApp.CreateBuilder();
                Constants.HostInfo = CreateHostInfo(appBuilder.Configuration);
                ConfigureMauiApp(appBuilder);
            }
#if DEBUG
            // NOTE: It's enabled in Debug mode only hence there is no performance penalties in Release mode.
            EnableContainerValidation(appBuilder);
#endif
            var app = appBuilder.Build();
            StaticLog.Factory = app.Services.LoggerFactory();

            AppNonScopedServiceStarter.WarmupStaticServices(HostInfo);

            BlazorWebViewApp.Initialize(() => BuildBlazorViewAppInternal(app));

            SetupBlazorViewAppPostBuildRoutine();

            LoadingUI.MarkAppBuilt();

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
        using var _1 = Tracer.Region();
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
        var services = blazorViewAppBuilder.Services;
        var svp = app.Services;
        services.Replace(ServiceDescriptor.Singleton(svp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton(new ParentContainerAccessor(svp));
        var dispatcher = svp.GetRequiredService<IDispatcher>();
        services.AddSingleton(dispatcher);
    }

#if WINDOWS
    private static void FixStaticContentProvider()
    {
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
    }
#endif

    private static void ConfigureMauiApp(MauiAppBuilder builder)
    {
        using var _ = Tracer.Region();

        builder = builder
            .UseMauiBlazorApp<App>()
            .ConfigureMauiHandlers(static handlers
                => handlers.AddHandler<IBlazorWebView>(_ => new CustomBlazorWebViewHandler()))
            .ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            })
            .ConfigureLifecycleEvents(ConfigurePlatformLifecycleEvents)
            .UseAppLinks();

        var services = builder.Services;

        // Core services
        services.AddSingleton(HostInfo);
        services.AddSingleton(HostInfo.Configuration);
        services.AddMauiDiagnostics(true);
    }

    private static void ConfigureBlazorApp(BlazorWebViewAppBuilder builder)
    {
        using var _ = Tracer.Region();
        var services = builder.Services;
        // Core services
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(Tracer.Default);
        services.Add(GetDeviceIdProviderServiceDescriptor());
        // Core MAUI services
        services.AddMauiBlazorWebView();
        AddSafeJSRuntime(services);
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
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

    // ConfigureXxx

    private static void ConfigureBlazorWebViewAppServices(IServiceCollection services)
    {
        using var _ = Tracer.Region();

#if IOS
        // HTTP client
        services.RemoveAll<IHttpClientFactory>();
        services.AddSingleton(c => new NativeHttpClientFactory(c));
        services.AddSingleton<IHttpClientFactory>(c => c.GetRequiredService<NativeHttpClientFactory>());
        services.AddSingleton<IHttpMessageHandlerFactory>(c => c.GetRequiredService<NativeHttpClientFactory>());
#endif

        // All other (module) services
        ClientStartup.ConfigureServices(services, Constants.HostInfo, c => [new Module.MauiAppModule(c)]);

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
#endif

    private static partial void ConfigureBlazorWebViewAppPlatformServices(this IServiceCollection services);
    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events);

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => Log.LogInformation("Unhandled exception, isTerminating={IsTerminating}.\n{Exception}",
            e.IsTerminating,
            e.ExceptionObject);
}
