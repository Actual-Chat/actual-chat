using ActualChat.Hosting;
using Stl.Rpc;

namespace ActualChat.UI.Blazor.Services;

public enum RpcConnectionStateKind
{
    Unknown = 0,
    Connecting,
    Connected,
    Reconnecting,
}

public sealed record RpcConnectionState(
    RpcConnectionStateKind Kind,
    Moment ReconnectsAt = default,
    Moment ProducedAt = default)
{
    public static readonly RpcConnectionState Unknown = new(RpcConnectionStateKind.Unknown);
    public static readonly RpcConnectionState Connected = new(RpcConnectionStateKind.Connected);
}

public class ReconnectUI : IComputeService
{
    public static readonly RpcPeerState ConnectedState = new(true);
    public static IMomentClock Clock => CpuClock.Instance; // Must match RpcClientPeerReconnectDelayer.Clock!

    private readonly IMutableState<RpcConnectionState> _state;
    private RpcPeerStateMonitor? _rpcPeerStateMonitor;
    private RpcClientPeerReconnectDelayer? _rpcReconnectDelayer;

    private IServiceProvider Services { get; }
    private RpcPeerStateMonitor RpcPeerStateMonitor
        => _rpcPeerStateMonitor ??= Services.GetRequiredService<RpcPeerStateMonitor>();
    private RpcClientPeerReconnectDelayer RpcReconnectDelayer
        => _rpcReconnectDelayer ??= Services.GetRequiredService<RpcClientPeerReconnectDelayer>();

    public bool IsClient { get; }
    public IState<RpcConnectionState> State => _state;

    public ReconnectUI(IServiceProvider services)
    {
        Services = services;
        IsClient = services.GetRequiredService<HostInfo>().AppKind.IsClient();
        _state = services.StateFactory().NewMutable(
            IsClient
                ? RpcConnectionState.Unknown with { ProducedAt = Clock.Now }
                : RpcConnectionState.Connected,
            StateCategories.Get(GetType(), nameof (State)));
    }

    [ComputeMethod]
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
