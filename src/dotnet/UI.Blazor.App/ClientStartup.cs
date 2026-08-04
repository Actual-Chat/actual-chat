using ActualChat.Diff.Handlers;
using ActualChat.Hosting;
using ActualChat.Logging;
using ActualChat.Module;
using ActualChat.UI.Blazor.App.Components.Discover;
using ActualChat.UI.Blazor.App.Components.PlaceInfo;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Pages;
using ActualChat.UI.Blazor.App.Pages.Landing.Docs;
using ActualChat.UI.Blazor.App.Pages.Test;
using ActualChat.UI.Blazor.Components.Internal;
using ActualChat.UI.Blazor.Components.Requirements;
using ActualChat.UI.Blazor.Module;
using ActualChat.UI.Blazor.Pages;
using ActualChat.UI.Blazor.Pages.DiveInModalTestPage;
using ActualChat.UI.Blazor.Pages.Emails;
using ActualChat.UI.Blazor.Pages.ErrorBarrierTestPage;
using ActualChat.UI.Blazor.Pages.RenderSlotTestPage;
using ActualChat.UI.Module;
using ActualLab.Fusion.Client;
using ActualLab.Fusion.Client.Caching;
using ActualLab.Fusion.Client.Interception;
using ActualLab.Fusion.Internal;
using ActualLab.Fusion.Trimming;
using ActualLab.Interception;
using ActualLab.Interception.Trimming;
using ActualLab.Internal;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.App;

public static class ClientStartup
{
    public static void Initialize()
    {
        // AppContext feature switches
        // AppContext.SetSwitch("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", false);

        RuntimeInfo.IsServer = false;
        ApiContractsModuleInitializer.Load();
        CoreModuleInitializer.Initialize();

#if !DEBUG
        RpcDiagnosticsOptions.Default = RpcDiagnosticsOptions.Default with {
            CallTracerFactory = _ => null // No call tracing in release builds
        };
#endif
        RpcWebSocketClientOptions.Default = RpcWebSocketClientOptions.Default with {
            UseAutoFrameDelayerFactory = true,
        };
        RpcCallTimeouts.Default.Command = new RpcCallTimeouts(20, null); // 20s for connecting
        ComputedSynchronizer.Default = ComputedSynchronizer.Safe.Instance = new ComputedSynchronizer.Safe() {
            MaxSynchronizeDurationProvider = static _ => TimeSpan.FromSeconds(1),
        };

#if DEBUG
        if (Constants.DebugMode.LogAnyThrownException)
            FirstChanceExceptionLogger.Use(LogLevel.Warning);
        if (OSInfo.IsWebAssembly && CoreConstants.DebugMode.RpcCalls.LogExistingCacheEntryUpdates)
            RemoteComputeServiceInterceptor.Options.Default = new() {
                LogCacheEntryUpdateSettings = (LogLevel.Information, int.MaxValue),
            };
#endif
        // We use a single instance of the initial delay task - we want it to be
        // an absolute delay from the app start rather than a relative delay for each call.
        var tracer = Tracer.Default[typeof(ClientStartup)];
        var hitToCallDelayTask = Task
            .Delay(Constants.Rpc.RemoteComputedCache.HitToCallInitialDelay)
            .ContinueWith(_ => {
                    RemoteComputedCache.HitToCallDelayer = null;
                    tracer.Point("RemoteComputedCache.HitToCallDelayer removed");
                }, // And no more delays after the initial one
                TaskScheduler.Default);
        RemoteComputedCache.HitToCallDelayer = (input, peer) => {
            // tracer.Point($"HitToCallDelayer.Invoke for {input}");
            return hitToCallDelayTask;
        };
    }

    [RequiresUnreferencedCode(UnreferencedCode.Reflection)]
    public static HostInfo CreateHostInfo(
        IConfiguration cfg,
        string environment,
        string deviceModel,
        HostKind hostKind,
        AppKind appKind,
        string baseUrl,
        bool isTested = false)
        => new() {
            Configuration = cfg,
            Environment = environment.NullIfEmpty() ?? Environments.Development,
            DeviceModel = deviceModel,
            HostKind = hostKind,
            AppKind = appKind,
            Roles = HostRoles.App,
            BaseUrl = baseUrl,
            IsTested = isTested,
        };

    [RequiresUnreferencedCode(UnreferencedCode.Reflection)]
    public static void ConfigureServices(
        IServiceCollection services,
        HostInfo hostInfo,
        Func<IServiceProvider, HostModule[]>? platformModuleFactory,
        Tracer? rootTracer = null)
    {
        var tracer = (rootTracer ?? Tracer.Default)[typeof(ClientStartup)];
        var hostKind = hostInfo.HostKind;

#if !DEBUG
        Interceptor.Options.Defaults.IsValidationEnabled = false;
#else
        if (hostKind.IsMauiApp())
            Interceptor.Options.Defaults.IsValidationEnabled = false;
#endif

        // Logging
        if (!hostKind.IsMauiApp()) // MauiDiagnostics takes care of that
            services.AddLogging(logging => {
                logging.ConfigureClientFilters(hostInfo.AppKind);
                logging.AddTailLogger();
                logging.AddSanitizingLoggerFactory(c => c.HostInfo().IsProductionInstance);
            });

        // Other services shared with plugins
        services.AddSingleton(hostInfo);
        services.AddSingleton(hostInfo.Configuration);

        // Creating modules
        using var _ = tracer.Region($"{nameof(ModuleHostBuilder)}.{nameof(ModuleHostBuilder.Build)}");
        var moduleServices = services.BuildServiceProvider();
        var moduleHostBuilder = new ModuleHostBuilder()
            // From less dependent to more dependent!
            .AddModules(
                // Core modules
                new CoreModule(moduleServices),
                // API
                new ApiModule(moduleServices),
                new ApiContractsModule(moduleServices),
                // UI modules
                new UICoreModule(moduleServices),
                new BlazorUICoreModule(moduleServices),
                // This module should be the last one
                new BlazorUIAppModule(moduleServices)
            );
        if (platformModuleFactory != null)
            moduleHostBuilder = moduleHostBuilder.AddModules(platformModuleFactory.Invoke(moduleServices));
        moduleHostBuilder.Build(services);

        if (hostInfo.AppKind == AppKind.Wasm)
            AugmentJSRuntime(services);
    }

    // Private methods

    private static void AugmentJSRuntime(IServiceCollection services)
    {
        // NOTE(AOT): Injection of source-gen JSON contexts is disabled for now, see InjectJsonTypeInfoResolvers
#if false
        var jsRuntimeRegistration = services.FirstOrDefault(c => c.ServiceType == typeof(IJSRuntime));
        if (jsRuntimeRegistration == null)
            return;

        var jsRuntimeType = jsRuntimeRegistration.ImplementationType;
        if (jsRuntimeType == null)
            return;

        // services.Remove(jsRuntimeRegistration);
        services.Add(new ServiceDescriptor(
            typeof(IJSRuntime),
            c => {
                var jsRuntime = (IJSRuntime)ActivatorUtilities.CreateInstance(c, jsRuntimeType);
                jsRuntime.InjectJsonTypeInfoResolvers();
                return jsRuntime;
            },
            jsRuntimeRegistration.Lifetime));
#endif
    }
}
