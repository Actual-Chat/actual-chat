using ActualChat.Rpc;
using ActualChat.Users;
using ActualLab.Fusion.Extensions;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Moves the client to the next RPC endpoint when the current one connects but
/// cannot carry traffic — the failure mode <see cref="RpcPeerState"/> can't see.
/// </summary>
public sealed class RpcEndpointMonitor(UIHub hub) : UIWorkerBase<UIHub>(hub)
{
    // Must exceed the ~16KB some networks let through before capping a connection,
    // otherwise a fully throttled link passes the probe.
    private const int ProbeSize = 64 * 1024;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ProbeRetryDelay = TimeSpan.FromSeconds(1);
    // The probe costs the user traffic and tells us nothing a working app doesn't already
    // show, so it waits until startup is over rather than competing with it.
    private static readonly TimeSpan ProbeDelay = TimeSpan.FromSeconds(60);
    private int _verifiedVersion = -1;
    private ISystemProperties SystemProperties => field ??= Services.GetRequiredService<ISystemProperties>();
    private ReconnectUI ReconnectUI => field ??= Services.GetRequiredService<ReconnectUI>();
    private RpcClientPeer? Peer => Hub.RpcHub.GetClientPeer(RpcRef.Default);

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        if (RpcEndpointSelector.Instance is null)
            return Task.CompletedTask;

        return AsyncChain.From(MonitorEndpoint)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(1, 30), Log)
            .CycleForever()
            .Run(cancellationToken);
    }

    // Private methods

    private async Task MonitorEndpoint(CancellationToken cancellationToken)
    {
        var selector = RpcEndpointSelector.Instance!;
        var state = ReconnectUI.State;
        await state.Computed.When(x => x.IsConnected, cancellationToken).ConfigureAwait(false);
        if (selector.Version == _verifiedVersion) {
            await WaitUntilDisconnected(cancellationToken).ConfigureAwait(false);
            return;
        }

        await Clocks.CpuClock.Delay(ProbeDelay, cancellationToken).ConfigureAwait(false);
        if (!state.Value.IsConnected)
            return;

        var version = selector.Version;
        if (await IsEndpointUsable(cancellationToken).ConfigureAwait(false)) {
            _verifiedVersion = version;
            await WaitUntilDisconnected(cancellationToken).ConfigureAwait(false);
            return;
        }

        MoveToNextEndpoint(selector);
    }

    private Task WaitUntilDisconnected(CancellationToken cancellationToken)
        => ReconnectUI.State.Computed.When(x => !x.IsConnected, cancellationToken);

    private async Task<bool> IsEndpointUsable(CancellationToken cancellationToken)
    {
        if (await Probe(cancellationToken).ConfigureAwait(false))
            return true;

        // One failure can be an ordinary blip; only a repeat means the endpoint is unusable.
        await Clocks.CpuClock.Delay(ProbeRetryDelay, cancellationToken).ConfigureAwait(false);
        return await Probe(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> Probe(CancellationToken cancellationToken)
    {
        var startedAt = Clocks.CpuClock.Now;
        try {
            using var cts = cancellationToken.CreateLinkedTokenSource();
            cts.CancelAfter(ProbeTimeout);
            var payload = await SystemProperties.GetProbePayload(ProbeSize, cts.Token).ConfigureAwait(false);
            var elapsed = Clocks.CpuClock.Now - startedAt;
            if (payload.Length < ProbeSize) {
                Log.LogWarning("RPC endpoint probe returned {Size} of {ExpectedSize} bytes",
                    payload.Length, ProbeSize);
                return false;
            }

            Log.LogInformation("RPC endpoint probe passed: {Size} bytes in {Elapsed}",
                ProbeSize, elapsed.ToShortString());
            return true;
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
            var elapsed = Clocks.CpuClock.Now - startedAt;
            Log.LogWarning(e, "RPC endpoint probe failed after {Elapsed}", elapsed.ToShortString());
            return false;
        }
    }

    private void MoveToNextEndpoint(RpcEndpointSelector selector)
    {
        if (selector.MoveNext())
            Log.LogWarning("Switching to the next RPC endpoint");
        else {
            // Every endpoint failed, so the network is likely down rather than the endpoint bad.
            Log.LogWarning("No usable RPC endpoint left, going back to the origin");
            selector.UseDirect();
        }

        Peer?.Disconnect();
        ReconnectUI.ResetReconnectDelays();
    }
}
