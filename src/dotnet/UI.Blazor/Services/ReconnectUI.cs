using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.Services;

public class ReconnectUI(UIHub hub)
    : RpcPeerStateMonitor(hub.Services, hub.HostInfo.HostKind.IsApp() ? RpcPeerRef.Default : null, false)
{
    private readonly TimeSpan _maxKeepAliveDelayOnDeviceAwake = hub.RpcHub.Limits.KeepAlivePeriod * 1.5;
    private TimeSpan _lastTotalSleepDuration = TimeSpan.Zero;

    private UIHub Hub { get; } = hub;
    private RpcClientPeerReconnectDelayer RpcReconnectDelayer
        => field ??= RpcHub.InternalServices.ClientPeerReconnectDelayer;

    public bool IsClient => Hub.HostInfo.HostKind.IsApp();
    public Moment SystemNow => Now;

    public RpcClientPeer? GetPeer()
    {
        try {
            return IsClient ? RpcHub.GetClientPeer(RpcPeerRef.Default) : null;
        }
        catch {
            // Intended
            return null;
        }
    }

    public void ReconnectIfDisconnected()
    {
        if (!IsClient)
            return;

        if (!State.Value.IsConnected)
            RpcReconnectDelayer.CancelDelays();
    }

    public void ResetReconnectDelays()
    {
        if (GetPeer() is not { } peer)
            return;

        peer.ResetTryIndex();
        RpcReconnectDelayer.CancelDelays();
    }

    public void TryReconnectOnDeviceAwake(TimeSpan totalSleepDuration)
    {
        TimeSpan sleepDuration;
        lock (Lock) {
            sleepDuration = totalSleepDuration - _lastTotalSleepDuration;
            _lastTotalSleepDuration = totalSleepDuration;
        }
        if (GetPeer() is not { } peer || !peer.IsConnected())
            return;

        var keepAliveDelay = Moment.Now - peer.LastKeepAliveAt;
        if (keepAliveDelay < _maxKeepAliveDelayOnDeviceAwake)
            return;

        Log.LogInformation(
            "Reconnecting on device awake ({SleepDuration} of sleep, {LastKeepAliveDelay} keep-alive delay)",
            sleepDuration.ToShortString(), keepAliveDelay.ToShortString());
        peer.ResetTryIndex();
        _ = peer.Disconnect();
    }

}
