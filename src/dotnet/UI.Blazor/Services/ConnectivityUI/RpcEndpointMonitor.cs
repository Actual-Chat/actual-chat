using ActualChat.Module;
using ActualChat.Rpc;
using ActualChat.UI.Blazor.Module;
using ActualChat.Users;
using ActualLab.Fusion.Extensions;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Keeps the client on an RPC endpoint that can actually carry traffic — the failure
/// mode <see cref="RpcPeerState"/> can't see. It picks one up front by measuring the
/// candidates, and demotes one that degrades later.
/// </summary>
public sealed class RpcEndpointMonitor(UIHub hub) : UIWorkerBase<UIHub>(hub)
{
    // Must exceed the ~16KB some networks let through before capping a connection,
    // otherwise a fully throttled link passes the probe.
    private const int ProbeSize = 64 * 1024;
    // Selection runs before the app is usable, so this deadline is what a user on a
    // restricted network waits out. 64KB in 6s is ~87 kbps - far above a capped link,
    // and low enough that a weak but working one still keeps the origin.
    private static readonly TimeSpan SelectTimeout = TimeSpan.FromSeconds(6);
    // A healthy origin answers well inside this, so the relays are dialed only once the
    // origin is already in doubt - on a good network they cost no traffic at all.
    private static readonly TimeSpan SelectHedgeDelay = TimeSpan.FromSeconds(1.5);
    // Measuring every relay at once has them competing for the same bottleneck, so the
    // measurement would degrade with each relay added. Only the nearest few are measured.
    private const int MaxThroughputProbes = 2;
    private static readonly TimeSpan RoundTripTimeout = TimeSpan.FromSeconds(3);
    // A material shift in min-RTT means the route changed - a handover, a new cell, a
    // different exit - which is when an earlier measurement stops describing the link.
    // It says nothing about which endpoint is better, only that the question is worth
    // asking again, so the cost of being wrong is one measurement.
    private static readonly TimeSpan PathCheckPeriod = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinPathChange = TimeSpan.FromMilliseconds(150);
    private const double PathChangeFactor = 3;
    private static readonly TimeSpan MinReselectInterval = TimeSpan.FromMinutes(2);
    // 64KB in 10s is ~52 kbps. Well under what a weak but usable link sustains, and well
    // over a capped one, which needs ~37s for this payload. A tighter deadline would
    // demand ~175 kbps and start failing links that are merely slow.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProbeRetryDelay = TimeSpan.FromSeconds(1);
    // This probe mostly guards against an endpoint that degrades mid-session - selection
    // already vetted it - so it waits for startup traffic to clear rather than competing
    // with it. It stays short because it's also the whole fallback when measuring can't run.
    private static readonly TimeSpan ProbeDelay = TimeSpan.FromSeconds(10);
    // The probe only runs once connected, so an endpoint that never connects would trap
    // us here - including the origin, which is exactly the case a relay exists for.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly string JSSetRpcBaseUriMethod
        = $"{BlazorUICoreModule.ImportName}.BrowserInit.setRpcBaseUri";
    private static readonly TimeSpan PushToJSTimeout = TimeSpan.FromSeconds(2);
    // Enough to smooth a single unlucky sample without making the reading lag a real change.
    private const int MeanRoundTripSampleCount = 3;
    private readonly Dictionary<string, EndpointMeasurement> _measurements = new(StringComparer.OrdinalIgnoreCase);
    private int _selectedVersion = -1;
    private int _verifiedVersion = -1;
    private int _measuredVersion = -1;
    private string _pushedEndpoint = "";
    private RpcEndpointReport? _pendingReport;
    private Moment _selectedAt;
    private ISystemProperties SystemProperties => field ??= Services.GetRequiredService<ISystemProperties>();
    private RpcServerProbe ServerProbe => field ??= Services.GetRequiredService<RpcServerProbe>();
    private ReconnectUI ReconnectUI => field ??= Services.GetRequiredService<ReconnectUI>();
    private ConnectivityUI ConnectivityUI => field ??= Services.GetRequiredService<ConnectivityUI>();
    private IAppActivityState ActivityState => field ??= Services.GetRequiredService<IAppActivityState>();
    // Optional: a headless scope may run without it, and then only a network change re-measures.
    private ServerTimeSync? TimeSync => field ??= Services.GetService<ServerTimeSync>();
    private RpcClientPeer? Peer => Hub.RpcHub.GetClientPeer(RpcRef.Default);
    private bool IsBackgroundIdle => ActivityState.State.Value == AppActivityState.BackgroundIdle;
    private MutableState<ImmutableArray<EndpointInfo>> MutableEndpoints
        => field ??= StateFactory.NewMutable(GetEndpoints());
    public IState<ImmutableArray<EndpointInfo>> Endpoints => MutableEndpoints;

    public async Task MeasureEndpoints(CancellationToken cancellationToken)
    {
        // The unsized probe is 2 bytes, so this times distance rather than throughput - the
        // reading stays meaningful on a link too slow to carry the app.
        if (RpcEndpointSelector.Instance is not { } selector)
            return;

        var candidates = selector.Candidates;
        var roundTripTasks = candidates
            .Select(x => ServerProbe.MeasureRoundTrip(x, RoundTripTimeout, cancellationToken))
            .ToArray();
        var roundTrips = await Task.WhenAll(roundTripTasks).ConfigureAwait(false);
        var current = selector.Current;
        ImmutableArray<EndpointInfo> endpoints;
        lock (_measurements) {
            DropStaleMeasurements(selector);
            for (var i = 0; i < candidates.Count; i++) {
                var measurement = GetMeasurement(candidates[i]);
                measurement.IsReachable = roundTrips[i] is not null;
                if (roundTrips[i] is { } roundTrip)
                    measurement.RoundTrip.AppendSample(roundTrip.TotalMilliseconds);
            }
            endpoints = [..candidates.Select(x => ToEndpointInfo(x, current))];
        }
        MutableEndpoints.Value = endpoints;
    }

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

    // It's internal to be accessible from tests
    internal static bool IsPathChange(TimeSpan before, TimeSpan after)
    {
        // Both directions count: a route that got worse may have started capping traffic,
        // and one that got better may mean a relay is no longer the best way out.
        if (before <= TimeSpan.Zero || after <= TimeSpan.Zero)
            return false;

        var ratio = after.TotalSeconds / before.TotalSeconds;
        return (after - before).Duration() >= MinPathChange
            && (ratio >= PathChangeFactor || ratio <= 1 / PathChangeFactor);
    }

    // Private methods

    private async Task MonitorEndpoint(CancellationToken cancellationToken)
    {
        var selector = RpcEndpointSelector.Instance!;
        var state = ReconnectUI.State;
        // A backgrounded app has its networking suspended, so a failure seen there says
        // nothing about the endpoint. BackgroundActive is different: a foreground service
        // (PTT, sync, recording) keeps the network, and that is when connectivity matters
        // most - so only BackgroundIdle is excluded.
        await ActivityState.State.Computed
            .When(x => x != AppActivityState.BackgroundIdle, cancellationToken)
            .ConfigureAwait(false);
        await SelectEndpoint(selector, cancellationToken).ConfigureAwait(false);
        await PushEndpointToJS(cancellationToken).ConfigureAwait(false);
        if (!await WaitUntilConnected(cancellationToken).ConfigureAwait(false)) {
            MoveToNextEndpoint(selector);
            return;
        }

        await ReportEndpoint(selector, cancellationToken).ConfigureAwait(false);
        if (selector.Version == _verifiedVersion) {
            await WaitWhileHealthy(cancellationToken).ConfigureAwait(false);
            return;
        }

        await Clocks.CpuClock.Delay(ProbeDelay, cancellationToken).ConfigureAwait(false);
        if (!state.Value.IsConnected)
            return;

        var version = selector.Version;
        if (await IsEndpointUsable(cancellationToken).ConfigureAwait(false))
            _verifiedVersion = version;
        else if (!IsBackgroundIdle) {
            // Slipping into the background mid-probe means we have no verdict rather than
            // a bad one, so nothing is recorded and the next connection tries again.
            MoveToNextEndpoint(selector);
            return;
        }

        await WaitWhileHealthy(cancellationToken).ConfigureAwait(false);
    }

    private async Task SelectEndpoint(RpcEndpointSelector selector, CancellationToken cancellationToken)
    {
        // Waiting for the connection to prove itself bad costs the better part of a minute,
        // during which the app is unusable and looks broken. Measuring the candidates
        // directly answers the same question in about a second.
        var version = selector.Version;
        if (version == _selectedVersion)
            return;

        var selection = await FindBestEndpoint(selector, cancellationToken).ConfigureAwait(false);
        if (selection is null && ServerProbe.IsSizedProbeSupported) {
            // Nothing answered, and the server can measure - so the network is simply down.
            // That's no verdict rather than a bad one; the next cycle tries again.
            return;
        }

        _selectedVersion = version;
        _selectedAt = Clocks.CpuClock.Now;
        // A server predating the sized probe can't be measured at all, so this falls back to
        // the older rule: a new network gets the origin, and a failing probe demotes us again.
        var endpoint = selection?.Endpoint ?? selector.OriginHost;
        _pendingReport = new RpcEndpointReport(endpoint,
            selection is null ? RpcEndpointReason.Unmeasurable : RpcEndpointReason.Measured,
            ToMilliseconds(selection?.OriginElapsed),
            ToMilliseconds(selection?.EndpointElapsed));
        if (endpoint == selector.Current)
            return;

        Log.LogWarning("Selected RPC endpoint: {Endpoint}", endpoint);
        selector.Use(endpoint);
        _verifiedVersion = -1;
        Peer?.Disconnect();
        ReconnectUI.ResetReconnectDelays();
    }

    private async Task<Selection?> FindBestEndpoint(
        RpcEndpointSelector selector,
        CancellationToken cancellationToken)
    {
        var candidates = selector.Candidates;
        if (candidates.Count < 2)
            return null;

        var origin = selector.OriginHost;
        using var cts = cancellationToken.CreateLinkedTokenSource();
        try {
            var originTask = ServerProbe.MeasureTransfer(origin, ProbeSize, SelectTimeout, cts.Token);
            var hedgeTask = Clocks.CpuClock.Delay(SelectHedgeDelay, cancellationToken);
            var completedTask = await Task.WhenAny(originTask, hedgeTask).ConfigureAwait(false);
            if (completedTask == originTask && await originTask.ConfigureAwait(false) is { } fast)
                return new Selection(origin, fast, fast);

            var relays = await ShortlistRelays(candidates, cts.Token).ConfigureAwait(false);
            var probeTasks = relays
                .Select(x => ServerProbe.MeasureTransfer(x, ProbeSize, SelectTimeout, cts.Token))
                .ToArray();
            var elapsed = await Task.WhenAll(probeTasks).ConfigureAwait(false);
            var originElapsed = await originTask.ConfigureAwait(false);
            // The origin wins whenever it works at all: a relay adds a hop and is shared by
            // everyone who needs one, so it's only worth taking when the direct path isn't.
            if (originElapsed is { } slow)
                return new Selection(origin, slow, slow);

            var bestIndex = -1;
            for (var i = 0; i < elapsed.Length; i++)
                if (elapsed[i] is { } x && (bestIndex < 0 || x < elapsed[bestIndex]!.Value))
                    bestIndex = i;

            return bestIndex < 0 ? null : new Selection(relays[bestIndex], null, elapsed[bestIndex]);
        }
        finally {
            cts.Cancel();
        }
    }

    private async Task<string[]> ShortlistRelays(
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        var relays = candidates.Skip(1).ToArray();
        if (relays.Length <= MaxThroughputProbes)
            return relays;

        var rttTasks = relays
            .Select(x => ServerProbe.MeasureRoundTrip(x, RoundTripTimeout, cancellationToken))
            .ToArray();
        var rtt = await Task.WhenAll(rttTasks).ConfigureAwait(false);
        var nearest = relays.Zip(rtt)
            .Where(x => x.Second is not null)
            .OrderBy(x => x.Second!.Value)
            .Take(MaxThroughputProbes)
            .Select(x => x.First)
            .ToArray();
        // Nothing answered the cheap probe, yet the expensive one may still get through -
        // so fall back to the declared order rather than giving up on every relay at once.
        return nearest.Length > 0 ? nearest : relays.Take(MaxThroughputProbes).ToArray();
    }

    private async Task ReportEndpoint(RpcEndpointSelector selector, CancellationToken cancellationToken)
    {
        // Reported once per connection rather than per decision, so the metric counts
        // connections - an endpoint carried over from an earlier decision still shows up.
        var report = _pendingReport ?? new RpcEndpointReport(selector.Current, RpcEndpointReason.Retained);
        _pendingReport = null;
        try {
            await SystemProperties.ReportRpcEndpoint(report, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
            Log.LogWarning(e, "Failed to report the RPC endpoint");
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

    private async Task WaitWhileHealthy(CancellationToken cancellationToken)
    {
        // Parking here until the connection drops is what left a mid-session change
        // undetected: a link that degrades without dropping never reaches a decision point.
        using var cts = cancellationToken.CreateLinkedTokenSource();
        var whenDisconnected = ReconnectUI.State.Computed.When(x => !x.IsConnected, cts.Token);
        var baseline = TimeSync?.MinRtt;
        try {
            while (true) {
                var delayTask = Clocks.CpuClock.Delay(PathCheckPeriod, cts.Token);
                if (await Task.WhenAny(whenDisconnected, delayTask).ConfigureAwait(false) == whenDisconnected)
                    return;

                if (!HasPathChanged(baseline))
                    continue;

                Log.LogWarning("RPC path changed: min-RTT {Before} -> {After}, re-measuring endpoints",
                    baseline?.ToShortString(), TimeSync?.MinRtt?.ToShortString());
                _selectedVersion = -1;
                _verifiedVersion = -1;
                return;
            }
        }
        finally {
            cts.Cancel();
        }
    }

    private bool HasPathChanged(TimeSpan? baseline)
    {
        if (baseline is not { } before || TimeSync?.MinRtt is not { } now)
            return false;
        if (Clocks.CpuClock.Now - _selectedAt < MinReselectInterval) {
            // A link whose RTT swings keeps clearing the bar, and each crossing costs a
            // full re-measurement, so the rate is capped rather than the signal softened.
            return false;
        }

        return IsPathChange(before, now);
    }

    private void MoveToNextEndpoint(RpcEndpointSelector selector)
    {
        if (selector.MoveNext()) {
            Log.LogWarning("Switching to the next RPC endpoint");
        }
        else {
            // Every endpoint failed, so the network is likely down rather than the endpoint
            // bad. UseDirect also expires the selection, so the next cycle re-measures.
            Log.LogWarning("No usable RPC endpoint left, going back to the origin");
            selector.UseDirect();
        }
        _verifiedVersion = -1;
        _pendingReport = new RpcEndpointReport(selector.Current, RpcEndpointReason.Demoted);
        Peer?.Disconnect();
        ReconnectUI.ResetReconnectDelays();
    }

    private ImmutableArray<EndpointInfo> GetEndpoints()
    {
        if (RpcEndpointSelector.Instance is not { } selector)
            return [];

        var current = selector.Current;
        lock (_measurements) {
            DropStaleMeasurements(selector);
            return [..selector.Candidates.Select(x => ToEndpointInfo(x, current))];
        }
    }

    private void DropStaleMeasurements(RpcEndpointSelector selector)
    {
        // A new network makes every earlier reading meaningless: the same host is a
        // different distance away once the route out changes.
        var version = selector.Version;
        if (version == _measuredVersion)
            return;

        _measuredVersion = version;
        _measurements.Clear();
    }

    private EndpointMeasurement GetMeasurement(string host)
    {
        if (!_measurements.TryGetValue(host, out var measurement))
            _measurements[host] = measurement = new EndpointMeasurement();

        return measurement;
    }

    private EndpointInfo ToEndpointInfo(string host, string current)
    {
        var measurement = _measurements.GetValueOrDefault(host);
        var meanRoundTrip = measurement is { RoundTrip.SampleCount: > 0 }
            ? TimeSpan.FromMilliseconds(measurement.RoundTrip.Value)
            : (TimeSpan?)null;
        return new EndpointInfo(host,
            string.Equals(host, current, StringComparison.OrdinalIgnoreCase),
            measurement?.IsReachable,
            meanRoundTrip);
    }

    private static double ToMilliseconds(TimeSpan? elapsed)
        => elapsed?.TotalMilliseconds ?? -1;

    // Nested types

    /// <summary>
    /// One RPC endpoint as a support readout: is it the one in use, does it
    /// answer, and how far away it is.
    /// </summary>
    public sealed record EndpointInfo(
        string Host,
        bool IsSelected,
        // Null rather than false until measured, so "not asked yet" doesn't read as "down".
        bool? IsReachable,
        TimeSpan? MeanRoundTrip);

    private sealed class EndpointMeasurement
    {
        public RunningEma RoundTrip { get; } = new(0, MeanRoundTripSampleCount);
        public bool? IsReachable { get; set; }
    }

    private sealed record Selection(string Endpoint, TimeSpan? OriginElapsed, TimeSpan? EndpointElapsed);

    private enum ProbeResult
    {
        Passed = 0,
        Failed,
        Inconclusive,
    }
}
