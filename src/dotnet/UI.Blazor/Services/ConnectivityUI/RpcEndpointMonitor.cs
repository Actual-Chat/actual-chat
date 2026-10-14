using ActualChat.Module;
using ActualChat.Rpc;
using ActualChat.UI.Blazor.Module;
using ActualChat.Users;
using ActualLab.Fusion.Extensions;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Keeps the client on an RPC endpoint that can actually carry traffic - the failure mode
/// <see cref="RpcPeerState"/> can't see. <see cref="RpcPathManager"/> decides, from what was checked
/// on this network before; this applies its decisions and tells it when a connection fails.
/// </summary>
public sealed class RpcEndpointMonitor : UIWorkerBase<UIHub>
{
    // The origin is dialed first, so on a restricted network this is what the user waits out
    // before moving to a relay
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    // A switch can land in the middle of a connection attempt, which then ends on the old endpoint
    private static readonly TimeSpan SwitchCheckPeriod = TimeSpan.FromSeconds(2);
    private static readonly string JSSetRpcBaseUriMethod
        = $"{BlazorUICoreModule.ImportName}.BrowserInit.setRpcBaseUri";
    private static readonly TimeSpan PushToJSTimeout = TimeSpan.FromSeconds(2);

    private readonly MutableState<ImmutableArray<EndpointInfo>> _endpoints;
    private RpcPathManager? _manager;
    private string _pushedEndpoint = "";
    private RpcEndpointReason _pendingReason = RpcEndpointReason.Retained;
    private Moment _switchedAt;
    private CancellationToken _runToken;
    private ReconnectUI ReconnectUI => field ??= Services.GetRequiredService<ReconnectUI>();
    private ConnectivityUI ConnectivityUI => field ??= Services.GetRequiredService<ConnectivityUI>();
    private IAppActivityState ActivityState => field ??= Services.GetRequiredService<IAppActivityState>();
    private ISystemProperties SystemProperties => field ??= Services.GetRequiredService<ISystemProperties>();
    private RpcClientPeer? Peer => Hub.RpcHub.GetClientPeer(RpcRef.Default);

    public IState<ImmutableArray<EndpointInfo>> Endpoints => _endpoints;

    public RpcEndpointMonitor(UIHub hub) : base(hub)
        // Created here rather than on first use: this worker and the diagnostics page both reach for
        // it off different threads, and a lazy would hand them two states on a race
        => _endpoints = StateFactory.NewMutable(GetEndpoints());

    public async Task MeasureEndpoints(CancellationToken cancellationToken)
    {
        if (_manager is not { } manager)
            return;

        await manager.CheckAll(cancellationToken).ConfigureAwait(false);
        _endpoints.Value = GetEndpoints();
    }

    // Protected/internal methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        if (RpcEndpointSelector.Instance is not { } selector
            || Services.GetService<RpcPathSettings>() is not { } settings)
            return;

        _runToken = cancellationToken;
        using var networkInfoProvider = new RpcNetworkInfoProvider(
            ConnectivityUI, ActivityState, selector, settings.GetNetworkId);
        var probe = new RpcPathProbe(Services);
        await using var manager = new RpcPathManager(
            settings.Candidates, settings.Store, networkInfoProvider, probe, Clocks.SystemClock, Log);
        _manager = manager;
        manager.CurrentChanged += OnCurrentChanged;
        manager.Updated += OnUpdated;
        manager.AttestationUpdated += selector.UseAttestation;
        manager.Start();
        try {
            _ = PushEndpointToJS(cancellationToken);
            var chains = new[] {
                AsyncChain.From(MonitorConnection),
                AsyncChain.From(KeepSwitchLanded),
            };
            await chains
                .Select(chain => chain.Log(LogLevel.Debug, Log).RetryForever(RetryDelaySeq.Exp(1, 30), Log))
                .RunIsolated(cancellationToken)
                .ConfigureAwait(false);
        }
        finally {
            manager.CurrentChanged -= OnCurrentChanged;
            manager.Updated -= OnUpdated;
            manager.AttestationUpdated -= selector.UseAttestation;
        }
    }

    // Private methods

    private async Task MonitorConnection(CancellationToken cancellationToken)
    {
        var selector = RpcEndpointSelector.Instance!;
        var state = ReconnectUI.State;
        while (true) {
            using var cts = cancellationToken.CreateLinkedTokenSource();
            cts.CancelAfter(ConnectTimeout);
            try {
                await state.Computed.When(x => x.IsConnected, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                // Being offline or idle in the background is not the endpoint's fault, and no
                // other endpoint would do better
                if (!ConnectivityUI.IsOnline.Value || ActivityState.State.Value == AppActivityState.BackgroundIdle)
                    continue;

                Log.LogWarning("RPC endpoint {Endpoint} did not connect within {Timeout}",
                    selector.Current, ConnectTimeout.ToShortString());
                _pendingReason = RpcEndpointReason.Demoted;
                await _manager!.ReportFailure(selector.Current).ConfigureAwait(false);
                continue;
            }

            await ReportEndpoint(selector, cancellationToken).ConfigureAwait(false);
            await state.Computed.When(x => !x.IsConnected, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task KeepSwitchLanded(CancellationToken cancellationToken)
    {
        var selector = RpcEndpointSelector.Instance!;
        while (true) {
            await Clocks.CpuClock.Delay(SwitchCheckPeriod, cancellationToken).ConfigureAwait(false);
            if (Clocks.CpuClock.Now - _switchedAt < SwitchCheckPeriod)
                continue;

            if (selector.ConnectHost is not { } connectHost
                || string.Equals(connectHost, selector.Current, StringComparison.OrdinalIgnoreCase))
                continue;

            // The last connection attempt began before the switch, so the peer is - or will be -
            // on the old endpoint, and nothing else is going to move it
            Log.LogWarning("RPC peer is on {ConnectHost}, not on {Endpoint}, moving it",
                connectHost, selector.Current);
            _ = Peer?.Disconnect();
            ReconnectUI.ResetReconnectDelays();
        }
    }

    private void OnCurrentChanged(string endpoint)
    {
        var selector = RpcEndpointSelector.Instance!;
        if (string.Equals(endpoint, selector.Current, StringComparison.OrdinalIgnoreCase))
            return;

        Log.LogWarning("Selected RPC endpoint: {Endpoint}", endpoint);
        if (_pendingReason != RpcEndpointReason.Demoted)
            _pendingReason = RpcEndpointReason.Measured;
        _switchedAt = Clocks.CpuClock.Now;
        selector.Use(endpoint);
        // ReSharper disable once MethodSupportsCancellation
        _ = Peer?.Disconnect();
        ReconnectUI.ResetReconnectDelays();
        _ = PushEndpointToJS(_runToken);
    }

    private void OnUpdated()
        => _endpoints.Value = GetEndpoints();

    private async Task ReportEndpoint(RpcEndpointSelector selector, CancellationToken cancellationToken)
    {
        // Reported once per connection rather than per decision, so the metric counts
        // connections - an endpoint carried over from an earlier decision still shows up.
        var report = new RpcEndpointReport(selector.Current, _pendingReason);
        _pendingReason = RpcEndpointReason.Retained;
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

    private ImmutableArray<EndpointInfo> GetEndpoints()
    {
        if (RpcEndpointSelector.Instance is not { } selector)
            return [];

        var current = selector.Current;
        var checks = _manager?.GetLatestChecks();
        var candidates = selector.Candidates;
        return [..candidates.Select((host, i) => {
            var check = checks?[i];
            return new EndpointInfo(host,
                string.Equals(host, current, StringComparison.OrdinalIgnoreCase),
                check?.IsOk,
                check is { IsOk: true } ? TimeSpan.FromMilliseconds(check.PingTimeMs) : null);
        })];
    }

    // Nested types

    /// <summary>
    /// One RPC endpoint as a support readout: is it the one in use, did its last check pass,
    /// and how long its ping took.
    /// </summary>
    public sealed record EndpointInfo(
        string Host,
        bool IsSelected,
        // Null rather than false until checked, so "not asked yet" doesn't read as "down".
        bool? IsReachable,
        TimeSpan? PingTime);
}
