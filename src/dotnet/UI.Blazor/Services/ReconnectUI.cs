using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Monitors RPC connection state and handles reconnection on disconnect or device wake.
/// </summary>
public sealed class ReconnectUI(UIHub hub)
    : RpcPeerStateMonitor(hub.Services, hub.HostInfo.HostKind.IsApp() ? RpcPeerRef.Default : null, false)
{
    private readonly TimeSpan _maxKeepAliveDelayOnDeviceAwake = hub.RpcHub.Limits.KeepAlivePeriod * 1.5;
    private TimeSpan _lastTotalSleepDuration = TimeSpan.Zero;

    private UIHub Hub { get; } = hub;
    private ConnectivityUI ConnectivityUI => Hub.ConnectivityUI;
    private RpcClientPeerReconnectDelayer RpcReconnectDelayer
        => field ??= RpcHub.InternalServices.ClientPeerReconnectDelayer;

    public Moment SystemNow => Now;

    public void ReconnectIfDisconnected()
    {
        if (ConnectivityUI.IsBlazorServer)
            return;
        if (!ConnectivityUI.IsOnline.Value)
            return; // No internet -> don't try to reconnect

        if (!State.Value.IsConnected)
            RpcReconnectDelayer.CancelDelays();
    }

    public void ResetReconnectDelays()
    {
        if (ConnectivityUI.Peer is not { } peer)
            return;

        peer.ResetConnectionAttemptIndex();
        RpcReconnectDelayer.CancelDelays();
    }

    public void TryReconnectOnDeviceAwake(TimeSpan totalSleepDuration)
    {
        TimeSpan sleepDuration;
        lock (Lock) {
            sleepDuration = totalSleepDuration - _lastTotalSleepDuration;
            _lastTotalSleepDuration = totalSleepDuration;
        }
        if (ConnectivityUI.Peer is not { } peer || !peer.ConnectionState.Value.IsConnected())
            return;

        var keepAliveDelay = Moment.Now - peer.LastKeepAliveAt;
        if (keepAliveDelay < _maxKeepAliveDelayOnDeviceAwake)
            return;

        Log.LogInformation(
            "Reconnecting on device awake ({SleepDuration} of sleep, {LastKeepAliveDelay} keep-alive delay)",
            sleepDuration.ToShortString(), keepAliveDelay.ToShortString());
        peer.ResetConnectionAttemptIndex();
        _ = peer.Disconnect();
    }
}
