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
using MemoryPack.Formatters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.App;

public static class ClientStartup
{
    // Libraries
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PriorityQueue<,>))] // MemoryPack uses it
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Range<>))] // JS dependency
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ImmutableOptionSet))] // Media.MetadataJson
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(OptionSet))] // Maybe some other JSON
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NewtonsoftJsonSerialized<>))] // Media.MetadataJson
    // Blazor
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DotNetObjectReference<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(EventCallback<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.JSInterop.Infrastructure.ArrayBuilder`1", "Microsoft.JSInterop")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.JSInterop.Infrastructure.DotNetObjectReferenceJsonConverter`1", "Microsoft.JSInterop")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(HeadOutlet))]
    // Diffs
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MissingDiffHandler<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ObjectDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(StringDiffHandler))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NullableDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RecordDiffHandler<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(OptionDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SetDiffHandler<,>))]
    // Test Pages
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DiscoverTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MarkupEditorTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlaceInfoTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AudioPlayerTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(BlazorTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(EmbeddedTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(JSTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ShareInModalTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ExternalContactsTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RequirementsTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DiveInModalTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(EmailTemplatesTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ErrorBarrierTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RenderSlotTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(FeaturesTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(InfoToastTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ReconnectOverlayTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WebSplashTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SkeletonsTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SvgCatsTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SystemTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TotpTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UIColorsTestPage))]
    // Pages
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlaceInfoPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DocsCookiesPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DocsFaqPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DocsPrivacyPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DocsTermsPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AdminCopyChatToPlacePage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AuthTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChatPage))]
    // [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(EmbeddedChatPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UserPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UnavailablePage))]
    // Components
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TextInputOptions))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChatView))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VirtualList<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VirtualListData<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VirtualListRenderState))]
    public static void Initialize()
    {
        // AppContext feature switches
        // AppContext.SetSwitch("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", false);

        // CodeKeeper actions
        if (CodeKeeper.AlwaysFalse) {
            ProxyCodeKeeper.Extension = new AppProxyCodeKeeper();
            CodeKeeper.Keep<ParameterComparer>();
            CodeKeeper.Keep<ByValueParameterComparer>();
            CodeKeeper.Keep<ByItemParameterComparer>();
            CodeKeeper.Keep<ByItemSetParameterComparer>();
            CodeKeeper.Keep<ByNoneParameterComparer>();
            CodeKeeper.Keep<ByRefParameterComparer>();
            CodeKeeper.Keep<ByUuidParameterComparer>();
            CodeKeeper.Keep<DefaultParameterComparer>();
            CodeKeeper.Keep<ByVersionParameterComparer<long>>();
            CodeKeeper.Keep<ByIdAndVersionParameterComparer<ChatId, long>>();
            CodeKeeper.Keep<ByIdAndVersionParameterComparer<PlaceId, long>>();
            CodeKeeper.Keep<ByUuidAndVersionParameterComparer<long>>();

            CodeKeeper.Keep<DefaultLayout>();
            CodeKeeper.Keep<InterfaceImmutableDictionaryFormatter<PlaceId, ChatId>>();
        }

        // Rpc & Fusion defaults
        RuntimeInfo.IsServer = false;
        CoreSerializerAndRpcSetup.Configure(false);
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
            FirstChanceExceptionLogger.Use();
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
    }
}
