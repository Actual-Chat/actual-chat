using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.Services;

public class ReconnectUI(UIHub hub)
    : RpcPeerStateMonitor(hub.Services, hub.HostInfo().HostKind.IsApp() ? RpcPeerRef.Default : null, false)
{
    private Hub Hub { get; } = hub;
    private RpcClientPeerReconnectDelayer RpcReconnectDelayer
        => RpcHub.InternalServices.ClientPeerReconnectDelayer;

    public bool IsClient => Hub.HostInfo().HostKind.IsApp();
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
