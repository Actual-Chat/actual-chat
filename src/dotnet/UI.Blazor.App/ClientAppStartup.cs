using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualLab.Fusion.Client;
using ActualLab.Fusion.Client.Caching;
using ActualLab.Fusion.Client.Interception;
using ActualLab.Internal;
using ActualLab.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.App;

public static class ClientAppStartup
{
    public static void Initialize()
    {
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
