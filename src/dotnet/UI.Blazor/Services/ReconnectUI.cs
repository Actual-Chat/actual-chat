using ActualChat.Hosting;
using Stl.Rpc;

namespace ActualChat.UI.Blazor.Services;

public class ReconnectUI(IServiceProvider services)
{
    public static readonly RpcPeerState ConnectedState = new(true);

    private HostInfo? _hostInfo;
    private RpcPeerStateMonitor? _rpcPeerStateMonitor;
    private RpcClientPeerReconnectDelayer? _rpcReconnectDelayer;
    private IMomentClock? _clock;

    private HostInfo HostInfo => _hostInfo ??= services.GetRequiredService<HostInfo>();
    private RpcPeerStateMonitor RpcPeerStateMonitor
        => _rpcPeerStateMonitor ??= services.GetRequiredService<RpcPeerStateMonitor>();
    private RpcClientPeerReconnectDelayer RpcReconnectDelayer
        => _rpcReconnectDelayer ??= services.GetRequiredService<RpcClientPeerReconnectDelayer>();

    public bool IsClient => HostInfo.AppKind.IsClient();
    public IMomentClock Clock => _clock ??= IsClient ? RpcReconnectDelayer.Clock : services.Clocks().CpuClock;

    public RpcPeerState? State
        => IsClient ? RpcPeerStateMonitor.State.Value : ConnectedState;

    public ValueTask<RpcPeerState?> UseState(CancellationToken cancellationToken = default)
        => IsClient
            ? RpcPeerStateMonitor.State.Use(cancellationToken)
            : ValueTask.FromResult<RpcPeerState?>(ConnectedState);

    public void ReconnectIfDisconnected(TimeSpan? watchInterval = null)
    {
        if (!IsClient)
            return;

        if (RpcPeerStateMonitor.State.Value?.IsConnected == false)
            RpcReconnectDelayer.CancelDelays();
    }

    public void ReconnectWhenDisconnected(TimeSpan? watchInterval = null)
    {
        if (!IsClient)
            return;

        _ = Task.Run(async () => {
            using var cts = new CancellationTokenSource(watchInterval ?? TimeSpan.FromSeconds(1));
            await RpcPeerStateMonitor.State.When(x => x?.IsConnected == false, cts.Token).ConfigureAwait(false);
            RpcReconnectDelayer.CancelDelays();
        });
    }
}
