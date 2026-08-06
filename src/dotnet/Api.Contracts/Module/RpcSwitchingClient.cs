using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Module;

public sealed class RpcSwitchingClient : RpcAlternatingClient
{
    private const int StrikesToPromote = 3;
    private const string ProbePath = "/rpc/check";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private HttpClient? _probeClient;
    private readonly LazySlim<BackgroundStateTracker?> _backgroundStateTracker;

    private RpcWebSocketClient RpcWebSocketClient { get; }
    private RpcHttpClient RpcHttpClient { get; }
    private UrlMapper UrlMapper => field ??= Services.UrlMapper();

    public bool StartPromoted { get; init; }

    public RpcSwitchingClient(
        IServiceProvider services,
        RpcWebSocketClient rpcWebSocketClient,
        RpcHttpClient rpcHttpClient
        ) : base(services, rpcWebSocketClient, rpcHttpClient)
    {
        RpcWebSocketClient = rpcWebSocketClient;
        RpcHttpClient = rpcHttpClient;
        _backgroundStateTracker = LazySlim.New(() => {
            // BackgroundStateTracker may be scoped (web); resolving from this singleton's
            // root scope throws under ValidateScopes — treat as absent (i.e. foreground).
            try {
                return Services.GetService<BackgroundStateTracker>();
            }
            catch {
                return null;
            }
        });
    }

    public override Task<RpcConnection> ConnectRemote(
        RpcClientPeer clientPeer,
        RpcPeerConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        var state = GetOrAddState(clientPeer);
        if (state.IsPromoted) {
            state.LastClient = RpcHttpClient;
            state.LastConnectionStartedAt = Hub.SystemClock.Now;
            return RpcHttpClient.Connect(clientPeer, connectionState, cancellationToken);
        }

        state.ProbeTask = ProbeServer(cancellationToken);
        return base.ConnectRemote(clientPeer, connectionState, cancellationToken);
    }

    public override void OnConnectionStateChange(
        RpcClientPeer clientPeer,
        RpcPeerConnectionState connectionState)
    {
        var state = GetOrAddState(clientPeer);
        // Capture LastClient before base clears it in its Disconnected branch.
        var lastClient = state.LastClient;
        base.OnConnectionStateChange(clientPeer, connectionState);
        if (connectionState.IsConnected()) {
            state.Strikes = 0;
            state.ProbeTask = null;
            return;
        }
        if (connectionState.IsDisconnected())
            _ = HandleDisconnected(state, lastClient, connectionState);
    }

    // Protected and private methods

    protected override RpcAlternatingClient.State CreateState()
        => new State { IsPromoted = StartPromoted };

    private State GetOrAddState(RpcClientPeer clientPeer)
    {
        var existing = clientPeer.Extensions.KeylessGet<RpcAlternatingClient.State>();
        if (existing is State s)
            return s;

        s = (State)CreateState();
        clientPeer.Extensions.KeylessSet<RpcAlternatingClient.State>(s);
        return s;
    }

    private async Task HandleDisconnected(
        State state, RpcClient? lastClient, RpcPeerConnectionState connectionState)
    {
        try {
            if (state.IsPromoted)
                return;
            if (connectionState.Error is not TimeoutException)
                return;
            if (lastClient != RpcWebSocketClient)
                return;
            var isBackground = _backgroundStateTracker.Value is { IsBackground.Value: true };
            if (isBackground)
                return;
            var probeTask = state.ProbeTask;
            if (probeTask is null)
                return;

            bool reachable;
            try {
                reachable = await probeTask.ConfigureAwait(false);
            }
            catch {
                reachable = false;
            }
            state.ProbeTask = null;

            if (!reachable) {
                state.Strikes = 0;
                return;
            }

            state.Strikes++;
            if (state.Strikes < StrikesToPromote)
                return;

            state.IsPromoted = true;
            Log.LogWarning(
                "Switching RPC client from WebSocket to HTTP after {Strikes} consecutive WS connect timeouts",
                state.Strikes);
        }
        catch (Exception e) {
            Log.LogError(e, "HandleDisconnected failed");
        }
    }

    private async Task<bool> ProbeServer(CancellationToken cancellationToken)
    {
        var url = UrlMapper.BaseUrl.TrimSuffix("/") + ProbePath;
        var httpClient = _probeClient ??= RpcHttpClient.Options.HttpClientFactory.Invoke(Services);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ProbeTimeout);
        try {
            using var response = await httpClient.GetAsync(url, cts.Token).ConfigureAwait(false);
            var success = response.IsSuccessStatusCode;
            Log.LogWarning(
                "RPC probe to {Url}: {Status} ({StatusCode})",
                url, success ? "OK" : "FAILED", (int)response.StatusCode);
            return success;
        }
        catch (Exception e) {
            Log.LogWarning(e, "RPC probe to {Url}: FAILED", url);
            return false;
        }
    }

    // Nested types

    private new sealed class State : RpcAlternatingClient.State
    {
        public int Strikes { get; set; }
        public bool IsPromoted { get; set; }
        public Task<bool>? ProbeTask { get; set; }

        // RpcSwitchingClient drives promotion via Strikes/ProbeTask; we don't want the base
        // alternation logic to blacklist the WS client after a single failure.
        public override bool IsFailed(RpcClientPeer clientPeer, RpcPeerConnectionState connectionState)
            => false;
    }
}
