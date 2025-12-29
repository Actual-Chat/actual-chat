using ActualChat.Module;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;

namespace ActualChat.App.Maui.IosShareExt;

public static class ClientStartup
{
    public static void Initialize()
    {
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
    }
}
