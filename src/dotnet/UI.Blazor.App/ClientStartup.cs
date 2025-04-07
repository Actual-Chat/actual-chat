using ActualChat.Diff.Handlers;
using ActualChat.Hosting;
using ActualChat.Logging;
using ActualChat.Module;
using ActualChat.UI.Blazor.App.Components.ChatRoulette;
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
using ActualLab.Fusion.Client;
using ActualLab.Fusion.Client.Caching;
using ActualLab.Fusion.Client.Interception;
using ActualLab.Fusion.Trimming;
using ActualLab.Interception;
using ActualLab.Interception.Trimming;
using ActualLab.Internal;
using ActualLab.Rpc;
using ActualLab.Trimming;
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
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CloneDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NullableDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RecordDiffHandler<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(OptionDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SetDiffHandler<,>))]
    // Test Pages
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChatRoulettePage))]
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
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DocsTermsPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AdminContentIndexerSettingsPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AdminCopyChatToPlacePage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AdminUserInvitesPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AuthTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChatPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(EmbeddedChatPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UserInvitePage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UserPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UnavailablePage))]
    // Components
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TextInputOptions))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChatView))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VirtualList<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VirtualListData<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VirtualListRenderState))]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Covered by other attributes")]
    [UnconditionalSuppressMessage("Trimming", "IL2110", Justification = "Covered by other attributes")]
    [UnconditionalSuppressMessage("Trimming", "IL2111", Justification = "Covered by other attributes")]
    public static void Initialize()
    {
        // AppContext feature switches
        // AppContext.SetSwitch("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", false);

        // CodeKeeper actions
        CodeKeeper.AddFakeAction(() => {
            // Extra "keep code" calls should be added here

            // Hardcode the known comparer types to avoid trimming
            // var typeKeeper = CodeKeeper.Get<TypeCodeKeeper>();
            // typeKeeper.KeepType<ByValueParameterComparer>();
            // typeKeeper.KeepType<ByItemParameterComparer>();
            // typeKeeper.KeepType<ByItemSetParameterComparer>();
            // typeKeeper.KeepType<ByNoneParameterComparer>();
            // typeKeeper.KeepType<ByRefParameterComparer>();
            // typeKeeper.KeepType<ByUuidParameterComparer>();
            // typeKeeper.KeepType<DefaultParameterComparer>();
            // typeKeeper.KeepType<ByVersionParameterComparer<long>>();
            // typeKeeper.KeepType<ByIdAndVersionParameterComparer<ChatId, long>>();
            // typeKeeper.KeepType<ByIdAndVersionParameterComparer<PlaceId, long>>();
            // typeKeeper.KeepType<ByUuidAndVersionParameterComparer<long>>();
            CodeKeeper.CallSilently(() => _ = new ByValueParameterComparer().AreEqual(null, null));
            CodeKeeper.CallSilently(() => _ = new ByItemParameterComparer().AreEqual(null, null));
            CodeKeeper.CallSilently(() => _ = new ByItemSetParameterComparer().AreEqual(null, null));
            CodeKeeper.CallSilently(() => _ = new ByNoneParameterComparer().AreEqual(null, null));
            CodeKeeper.CallSilently(() => _ = new ByRefParameterComparer().AreEqual(null, null));
            CodeKeeper.CallSilently(() => _ = new ByUuidParameterComparer().AreEqual(null, null));
            CodeKeeper.CallSilently(() => _ = new DefaultParameterComparer().AreEqual(null, null));
            CodeKeeper.CallSilently(() => _ = new ByVersionParameterComparer<long>().AreEqual(null, null));
            CodeKeeper.CallSilently(() => _ = new ByIdAndVersionParameterComparer<ChatId, long>().AreEqual(null, null));
            CodeKeeper.CallSilently(() => _ = new ByIdAndVersionParameterComparer<PlaceId, long>().AreEqual(null, null));
            CodeKeeper.CallSilently(() => _ = new ByUuidAndVersionParameterComparer<long>().AreEqual(null, null));

            CodeKeeper.CallSilently(() => _ = new DefaultLayout());
            CodeKeeper.CallSilently(() => _ = new InterfaceImmutableDictionaryFormatter<PlaceId, ChatId>());
            // TODO: Add support for parameter comparers
        });
        CodeKeeper.Set<ProxyCodeKeeper, FusionProxyCodeKeeper>();
        if (CodeKeeper.AlwaysFalse) {
            // NOTE(AY): This block actually does nothing, it's just to measure the time RunActions() takes (if called).
            // Currently, any proxy uses .AddAction() to register its "actions", even though it's not needed -
            // .AddFakeAction() is enough for AOT code generation & IL trimmers.
            // So likely I'll remove .AddAction() and this block later.

            var now = CpuTimestamp.Now;
            CodeKeeper.RunActions(); // ~ 60ms, all due to JIT?
            Tracer.Default[nameof(CodeKeeper)].Point($"RunActions took {now.Elapsed.ToShortString()}");
        }

        // Rpc & Fusion defaults
        RpcDefaults.Mode = RpcMode.Client;
        FusionDefaults.Mode = FusionMode.Client;
        RpcDefaultDelegates.FrameDelayerProvider = RpcFrameDelayerProviders.Auto();
        RpcCallTimeouts.Defaults.Command = new RpcCallTimeouts(20, null); // 20s for connect
#if !DEBUG
        RpcSerializationFormatResolver.Default = RpcSerializationFormatResolver.Default with {
            DefaultClientFormatKey = "mempack2c", // "Compact", i.e. use method name hashes instead of actual names
        };
#endif
        RemoteComputedSynchronizer.Default = new RemoteComputedSynchronizer() {
            TimeoutFactory = (_, ct) => Task.Delay(TimeSpan.FromSeconds(1), ct),
        };

#if DEBUG
        if (Constants.DebugMode.LogAnyThrownException)
            FirstChanceExceptionLogger.Use();
        if (OSInfo.IsWebAssembly && Constants.DebugMode.RpcCalls.LogExistingCacheEntryUpdates)
            RemoteComputeServiceInterceptor.Options.Default = new() {
                LogCacheEntryUpdateSettings = (LogLevel.Information, int.MaxValue),
            };
#endif
        var remoteComputedCacheUpdateDelayTask = Task.Delay(Constants.RpcCalls.InitialCacheInvalidationDelay)
            .ContinueWith(_ => RemoteComputedCache.UpdateDelayer = (_, _) => Task.Delay(Constants.RpcCalls.CacheInvalidationDelay), TaskScheduler.Default);
        RemoteComputedCache.UpdateDelayer = (_, _) => remoteComputedCacheUpdateDelayTask;
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
            services.AddLogging(logging => logging.ConfigureClientFilters(hostInfo.AppKind).AddTailLogger());

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
                new BlazorUICoreModule(moduleServices),
                // This module should be the last one
                new BlazorUIAppModule(moduleServices)
            );
        if (platformModuleFactory != null)
            moduleHostBuilder = moduleHostBuilder.AddModules(platformModuleFactory.Invoke(moduleServices));
        moduleHostBuilder.Build(services);
    }
}
