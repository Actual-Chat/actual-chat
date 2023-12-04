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

    public void ReconnectIfDisconnected()
    {
        if (!IsClient)
            return;

        if (!State.Value.IsConnected)
            RpcReconnectDelayer.CancelDelays();
    }

    public void ResetReconnectDelays()
    {
        if (!IsClient)
            return;

        try {
            RpcHub.GetClientPeer(RpcPeerRef.Default).ResetTryIndex();
        }
        catch {
            // Intended
        }
        RpcReconnectDelayer.CancelDelays();
    }

}
