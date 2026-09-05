using ActualChat.Hosting;
using ActualChat.Module;
using ActualLab.Internal;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.App.Maui.IosShareExt;

// TODO: probably must be unified with UI.Blazor.App.ClientStartup
public static class ClientStartup
{
    public static void Initialize()
    {
        // Rpc & Fusion defaults
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
        var cacheFallbackDelay = Constants.Rpc.RemoteComputedCache.CacheFallbackDelay;
        RpcCallTimeouts.Default.Query = RpcCallTimeouts.Default.Query with { CacheFallbackDelay = cacheFallbackDelay };
        RpcCallTimeouts.Default.Debug = RpcCallTimeouts.Default.Debug with { CacheFallbackDelay = cacheFallbackDelay };
        ComputedSynchronizer.Default = ComputedSynchronizer.Safe.Instance = new ComputedSynchronizer.Safe() {
            MaxSynchronizeDurationProvider = static _ => TimeSpan.FromSeconds(1),
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
}
