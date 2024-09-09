using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Sources;
using ActualChat.Diff.Handlers;
using ActualChat.Hosting;
using ActualLab.Fusion.Client;
using ActualLab.Fusion.Client.Caching;
using ActualLab.Fusion.Client.Interception;
using ActualLab.Fusion.Trimming;
using ActualLab.Interception.Trimming;
using ActualLab.Internal;
using ActualLab.Rpc;
using ActualLab.Trimming;
using MemoryPack.Formatters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.App;

public static class ClientAppStartup
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
    // Diffs
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MissingDiffHandler<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CloneDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NullableDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RecordDiffHandler<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(OptionDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SetDiffHandler<,>))]
    public static void Initialize()
    {
        // AppContext feature switches
        // AppContext.SetSwitch("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", false);
        AppContext.SetSwitch("Switch.System.Reflection.ForceInterpretedInvoke", false);
        AppContext.SetSwitch("Microsoft.Extensions.DependencyInjection.DisableDynamicEngine", true);

        // CodeKeeper actions
        CodeKeeper.AddFakeAction(() => {
            // Extra "keep code" calls should be added here
            CodeKeeper.FakeCallSilently(() => _ = new DefaultLayout());
            CodeKeeper.CallSilently(() => _ = new InterfaceImmutableDictionaryFormatter<PlaceId, ChatId>());
            // TODO: Add support for parameter comparers
        });
        CodeKeeper.Set<ProxyCodeKeeper, FusionProxyCodeKeeper>();
        if (OSInfo.IsWindows) {
            // NativeAOT is used only on Windows app in our case
            var now = CpuTimestamp.Now;
            CodeKeeper.RunActions();
            Tracer.Default[nameof(CodeKeeper)].Point($"RunActions took {now.Elapsed.ToShortString()}");
        }

        // Rpc & Fusion defaults
        RpcDefaults.Mode = RpcMode.Client;
        FusionDefaults.Mode = FusionMode.Client;
        RpcCallTimeouts.Defaults.Command = new RpcCallTimeouts(20, null); // 20s for connect
        RemoteComputedSynchronizer.Default = new RemoteComputedSynchronizer() {
            TimeoutFactory = (_, ct) => Task.Delay(TimeSpan.FromSeconds(1), ct),
        };
#if DEBUG
        if (OSInfo.IsWebAssembly && Constants.DebugMode.RpcCalls.LogExistingCacheEntryUpdates)
            RemoteComputeServiceInterceptor.Options.Default = new() {
                LogCacheEntryUpdateSettings = (LogLevel.Information, int.MaxValue),
            };
#endif
        var remoteComputedCacheUpdateDelayTask = Task.Delay(2200)
            .ContinueWith(_ => RemoteComputedCache.UpdateDelayer = null, TaskScheduler.Default);
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
        Tracer? rootTracer = null)
    {
        // Logging
        services.AddLogging(logging => logging.ConfigureClientFilters(hostInfo.AppKind));

        // Other services shared with plugins
        services.AddSingleton(hostInfo);
        services.AddSingleton(hostInfo.Configuration);
        AppStartup.ConfigureServices(services, hostInfo.HostKind, null, rootTracer);
    }
}
