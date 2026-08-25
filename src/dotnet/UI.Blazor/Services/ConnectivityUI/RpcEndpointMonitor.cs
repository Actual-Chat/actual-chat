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
    // The probe only runs once connected, so an endpoint that never connects would trap
    // us here. The origin gets no deadline: there is nowhere better to go.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
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
        if (!await WaitUntilConnected(selector, cancellationToken).ConfigureAwait(false)) {
            MoveToNextEndpoint(selector);
            return;
        }
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

    private async Task<bool> WaitUntilConnected(
        RpcEndpointSelector selector,
        CancellationToken cancellationToken)
    {
        var state = ReconnectUI.State;
        if (selector.IsOnOrigin) {
            await state.Computed.When(x => x.IsConnected, cancellationToken).ConfigureAwait(false);
            return true;
        }

        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(ConnectTimeout);
        try {
            await state.Computed.When(x => x.IsConnected, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            Log.LogWarning("RPC endpoint did not connect within {Timeout}", ConnectTimeout.ToShortString());
            return false;
        }
    }

    private Task WaitUntilDisconnected(CancellationToken cancellationToken)
        => ReconnectUI.State.Computed.When(x => !x.IsConnected, cancellationToken);

    private async Task<bool> IsEndpointUsable(CancellationToken cancellationToken)
    {
        if (await Probe(cancellationToken).ConfigureAwait(false) != ProbeResult.Failed)
            return true;

        // One failure can be an ordinary blip; only a repeat means the endpoint is unusable.
        await Clocks.CpuClock.Delay(ProbeRetryDelay, cancellationToken).ConfigureAwait(false);
        return await Probe(cancellationToken).ConfigureAwait(false) != ProbeResult.Failed;
    }

    private async Task<ProbeResult> Probe(CancellationToken cancellationToken)
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
                return ProbeResult.Inconclusive;
            }

            Log.LogInformation("RPC endpoint probe passed: {Size} bytes in {Elapsed}",
                ProbeSize, elapsed.ToShortString());
            return ProbeResult.Passed;
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
            // A throttled link makes the payload arrive late or not at all, so only a
            // timeout indicts the endpoint. A fast failure means something a different
            // endpoint cannot fix - an older server without this method, say - and acting
            // on it would force reconnects that never help.
            var elapsed = Clocks.CpuClock.Now - startedAt;
            var isTimeout = elapsed >= ProbeTimeout;
            Log.LogWarning(e, "RPC endpoint probe {Outcome} after {Elapsed}",
                isTimeout ? "timed out" : "failed for an unrelated reason", elapsed.ToShortString());
            return isTimeout ? ProbeResult.Failed : ProbeResult.Inconclusive;
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

    // Nested types

    private enum ProbeResult
    {
        Passed = 0,
        Failed,
        Inconclusive,
    }
}
