using ActualChat.Rpc;
using ActualChat.UI.Blazor.Module;
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
    // 64KB in 10s is ~52 kbps. Well under what a weak but usable link sustains, and well
    // over a capped one, which needs ~37s for this payload. A tighter deadline would
    // demand ~175 kbps and start failing links that are merely slow.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProbeRetryDelay = TimeSpan.FromSeconds(1);
    // The probe costs the user traffic and tells us nothing a working app doesn't already
    // show, so it waits until startup is over rather than competing with it. It cannot wait
    // much longer: a degraded connection often drops within a minute, and a probe that
    // never fires on a bad link is worse than no probe at all.
    private static readonly TimeSpan ProbeDelay = TimeSpan.FromSeconds(20);
    // The probe only runs once connected, so an endpoint that never connects would trap
    // us here - including the origin, which is exactly the case a relay exists for.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly string JSSetRpcBaseUriMethod
        = $"{BlazorUICoreModule.ImportName}.BrowserInit.setRpcBaseUri";
    private static readonly TimeSpan PushToJSTimeout = TimeSpan.FromSeconds(2);
    private int _verifiedVersion = -1;
    private string _pushedEndpoint = "";
    private ISystemProperties SystemProperties => field ??= Services.GetRequiredService<ISystemProperties>();
    private ReconnectUI ReconnectUI => field ??= Services.GetRequiredService<ReconnectUI>();
    private ConnectivityUI ConnectivityUI => field ??= Services.GetRequiredService<ConnectivityUI>();
    private IAppActivityState ActivityState => field ??= Services.GetRequiredService<IAppActivityState>();
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

    private bool IsBackgroundIdle
        => ActivityState.State.Value == AppActivityState.BackgroundIdle;

    private async Task MonitorEndpoint(CancellationToken cancellationToken)
    {
        var selector = RpcEndpointSelector.Instance!;
        var state = ReconnectUI.State;
        await PushEndpointToJS(cancellationToken).ConfigureAwait(false);
        // A backgrounded app has its networking suspended, so a failure seen there says
        // nothing about the endpoint. BackgroundActive is different: a foreground service
        // (PTT, sync, recording) keeps the network, and that is when connectivity matters
        // most - so only BackgroundIdle is excluded.
        await ActivityState.State.Computed
            .When(x => x != AppActivityState.BackgroundIdle, cancellationToken)
            .ConfigureAwait(false);
        if (!await WaitUntilConnected(cancellationToken).ConfigureAwait(false)) {
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
        if (await IsEndpointUsable(cancellationToken).ConfigureAwait(false))
            _verifiedVersion = version;
        else if (!IsBackgroundIdle) {
            MoveToNextEndpoint(selector);
            return;
        }
        // Slipping into the background mid-probe means we have no verdict rather than a
        // good one, so nothing is recorded and the next connection tries again.

        await WaitUntilDisconnected(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> WaitUntilConnected(CancellationToken cancellationToken)
    {
        var state = ReconnectUI.State;
        while (true) {
            using var cts = cancellationToken.CreateLinkedTokenSource();
            cts.CancelAfter(ConnectTimeout);
            try {
                await state.Computed.When(x => x.IsConnected, cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                // Being offline or idle in the background is not the endpoint's fault, and
                // no other endpoint would do better, so keep waiting instead of cycling.
                if (!ConnectivityUI.IsOnline.Value || IsBackgroundIdle)
                    continue;

                Log.LogWarning("RPC endpoint did not connect within {Timeout}", ConnectTimeout.ToShortString());
                return false;
            }
        }
    }

    private async Task PushEndpointToJS(CancellationToken cancellationToken)
    {
        // The media workers read this when they start, so a switch reaches audio and video
        // on their next start - moving a live peer would mean rebuilding the pipeline.
        var endpoint = RpcEndpointSelector.ApplyTo(Services.UrlMapper().BaseUrl.TrimSuffix("/"));
        if (endpoint == _pushedEndpoint)
            return;

        try {
            using var cts = cancellationToken.CreateLinkedTokenSource();
            cts.CancelAfter(PushToJSTimeout);
            await JS.InvokeVoidAsync(JSSetRpcBaseUriMethod, cts.Token, endpoint).ConfigureAwait(false);
            _pushedEndpoint = endpoint;
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
            Log.LogWarning(e, "Failed to push the RPC endpoint to JS");
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
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(ProbeTimeout);
        try {
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
            // Ask the deadline itself: cancellation lands just before the measured elapsed
            // time reaches the timeout, so comparing the two never sees a timeout.
            var elapsed = Clocks.CpuClock.Now - startedAt;
            var isTimeout = cts.IsCancellationRequested;
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
