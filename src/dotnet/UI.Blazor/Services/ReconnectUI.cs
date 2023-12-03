using ActualChat.Hosting;
using Stl.Rpc;

namespace ActualChat.UI.Blazor.Services;

public class ReconnectUI : RpcPeerStateMonitor
{
    private Scope Scope { get; }
    private HostInfo HostInfo => Scope.HostInfo;
    private RpcClientPeerReconnectDelayer RpcReconnectDelayer
        => RpcHub.InternalServices.ClientPeerReconnectDelayer;

    public bool IsClient => Scope.HostInfo.AppKind.IsClient();
    public new Moment Now => base.Now;

    public ReconnectUI(Scope scope)
        : base(scope.Services, scope.HostInfo.AppKind.IsClient() ? RpcPeerRef.Default : null, false)
        => Scope = scope;

    public void ReconnectIfDisconnected(TimeSpan? watchInterval = null)
    {
        if (!IsClient)
            return;

        if (!State.Value.IsConnected)
            RpcReconnectDelayer.CancelDelays();
    }

    public void ReconnectWhenDisconnected() => ReconnectIfDisconnected(TimeSpan.FromSeconds(5));
    public void ReconnectWhenDisconnected(TimeSpan watchInterval)
    {
        if (!IsClient)
            return;

        _ = Task.Run(async () => {
            using var cts = new CancellationTokenSource(watchInterval);
            while (true) {
                await State.When(x => !x.IsConnected, cts.Token).ConfigureAwait(false);
                RpcReconnectDelayer.CancelDelays();
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token).ConfigureAwait(false);
            }
            // ReSharper disable once FunctionNeverReturns
        });
    }

}
