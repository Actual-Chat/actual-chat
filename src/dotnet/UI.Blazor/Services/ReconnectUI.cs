using ActualChat.Hosting;
using Stl.Rpc;

namespace ActualChat.UI.Blazor.Services;

public class ReconnectUI(UIHub hub)
    : RpcPeerStateMonitor(hub.Services, hub.HostInfo().AppKind.IsClient() ? RpcPeerRef.Default : null, false)
{
    private Hub Hub { get; } = hub;
    private RpcClientPeerReconnectDelayer RpcReconnectDelayer
        => RpcHub.InternalServices.ClientPeerReconnectDelayer;

    public bool IsClient => Hub.HostInfo().AppKind.IsClient();
    public Moment CpuNow => Now;

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
