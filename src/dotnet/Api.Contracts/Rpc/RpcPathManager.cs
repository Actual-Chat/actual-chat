namespace ActualChat.Rpc;

/// <summary>
/// Decides which host the RPC connection should use: connects to the best known one at once,
/// checks the others in the background when their records are stale, and moves to a host
/// that works when the current one doesn't.
/// </summary>
public sealed class RpcPathManager(
    IReadOnlyList<string> candidates,
    IRpcPathStore store,
    IRpcNetworkInfoProvider networkInfoProvider,
    IRpcPathProbe probe,
    MomentClock clock,
    ILogger? log = null
    ) : WorkerBase
{
    public static readonly TimeSpan RecheckPeriod = TimeSpan.FromHours(1);
    // Well under RpcRelayAttestation.MaxAge, so the origin never sees an expired one
    public static readonly TimeSpan AttestationRefreshPeriod = TimeSpan.FromSeconds(20);
    // A relay that doesn't answer in this time is switched to without an attestation
    public static readonly TimeSpan AttestationSwitchTimeout = TimeSpan.FromSeconds(1.5);

    private readonly Lock _lock = new();
    private RpcPathHistory _history = new();
    private string _networkId = "";
    private RpcNetworkInfo? _network;
    private TaskCompletionSource _whenReady = TaskCompletionSourceExt.New();
    private string _current = candidates[0];
    private int _cycleCount;
    private TaskCompletionSource<Signal> _signal = TaskCompletionSourceExt.New<Signal>();
    private CancellationTokenSource? _cycleTokenSource;

    private IReadOnlyList<string> Candidates { get; } = candidates;
    private IRpcPathStore Store { get; } = store;
    private IRpcNetworkInfoProvider NetworkInfoProvider { get; } = networkInfoProvider;
    private IRpcPathProbe Probe { get; } = probe;
    private MomentClock Clock { get; } = clock;
    private ILogger? Log { get; } = log;

    // False while offline or suspended
    public bool IsReady => Volatile.Read(ref _network) is not null;
    public string Current => Volatile.Read(ref _current);
    public int CycleCount => Volatile.Read(ref _cycleCount);

    public event Action<string>? CurrentChanged;
    public event Action? Updated;
    // What a relay said about this client's IP: the host and the attestation
    public event Action<string, string>? AttestationUpdated;

    // Called when the live connection to the current host can't be (re)established
    public async Task ReportFailure(string host)
    {
        if (!IsReady)
            return;

        await Record(host, null, CancellationToken.None).ConfigureAwait(false);
        _signal.TrySetResult(Signal.Failure);
    }

    // The newest check of each candidate, in the order of the candidates
    public IReadOnlyList<RpcPathCheck?> GetLatestChecks()
    {
        lock (_lock)
            return Candidates.Select(x => _history.GetChecks(x).LastOrDefault()).ToList();
    }

    // Checks every candidate now, whatever its record says: for a page that shows them
    public async Task CheckAll(CancellationToken cancellationToken)
    {
        // Before the first cycle there is no history of the network to add to
        if (!IsReady || CycleCount == 0)
            return;

        var tasks = Candidates.Select(x => CheckAndRecord(x, cancellationToken)).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    // Protected/internal methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        using var readNetworkTokenSource = cancellationToken.CreateLinkedTokenSource();
        var readNetworkTask = ReadNetwork(readNetworkTokenSource.Token);
        var keepAttestationTask = KeepAttestationFresh(readNetworkTokenSource.Token);
        try {
            var mustVerify = false;
            while (true) {
                await WhenReady(cancellationToken).ConfigureAwait(false);
                await RunCycle(mustVerify, cancellationToken).ConfigureAwait(false);
                var signal = await WaitForSignal(cancellationToken).ConfigureAwait(false);
                mustVerify = signal == Signal.NetworkChanged;
            }
        }
        finally {
            await readNetworkTokenSource.CancelAsync().ConfigureAwait(false);
            await readNetworkTask.SilentAwait(false);
            await keepAttestationTask.SilentAwait(false);
        }
    }

    // Private methods

    private async Task KeepAttestationFresh(CancellationToken cancellationToken)
    {
        while (true) {
            await Clock.Delay(AttestationRefreshPeriod, cancellationToken).ConfigureAwait(false);
            if (IsReady)
                await RefreshAttestation(Current, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshAttestation(string host, CancellationToken cancellationToken)
    {
        // The origin sees the client itself
        if (string.Equals(host, Candidates[0], StringComparison.OrdinalIgnoreCase))
            return;

        var attestation = await Probe.GetAttestation(host, cancellationToken).ConfigureAwait(false);
        if (attestation is not null)
            AttestationUpdated?.Invoke(host, attestation);
    }

    // The attestation goes first: the connection that follows the switch is the one the origin
    // sees the client's IP on, and it lasts until the next switch
    private async Task SwitchTo(string host, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(AttestationSwitchTimeout);
        try {
            await RefreshAttestation(host, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            // Switch without it
        }
        SetCurrent(host);
    }

    private async Task ReadNetwork(CancellationToken cancellationToken)
    {
        while (true) {
            try {
                await foreach (var network in NetworkInfoProvider.Watch(cancellationToken).ConfigureAwait(false))
                    SetNetwork(network);
                return;
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                Log?.LogWarning(e, "Failed to read the network info");
                await Clock.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void SetNetwork(RpcNetworkInfo? network)
    {
        bool hasNetworkChanged;
        lock (_lock) {
            Volatile.Write(ref _network, network);
            if (network is null) {
                if (_whenReady.Task.IsCompleted)
                    _whenReady = TaskCompletionSourceExt.New();
            }
            else
                _whenReady.TrySetResult();
            hasNetworkChanged = network is not null && _networkId.Length != 0 && _networkId != network.NetworkId;
        }
        if (hasNetworkChanged)
            OnNetworkChanged();
    }

    private async Task WhenReady(CancellationToken cancellationToken)
    {
        while (!IsReady) {
            Task whenReady;
            lock (_lock)
                whenReady = _whenReady.Task;
            await whenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunCycle(bool mustVerify, CancellationToken cancellationToken)
    {
        using var cycleTokenSource = cancellationToken.CreateLinkedTokenSource();
        Volatile.Write(ref _cycleTokenSource, cycleTokenSource);
        try {
            await LoadHistory(cycleTokenSource.Token).ConfigureAwait(false);
            var plan = Plan(mustVerify);
            await SwitchTo(plan.FirstHost, cycleTokenSource.Token).ConfigureAwait(false);
            await Execute(plan, cycleTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            // The network changed: the next cycle starts from its history
        }
        finally {
            Volatile.Write(ref _cycleTokenSource, null);
            Interlocked.Increment(ref _cycleCount);
        }
    }

    private async Task Execute(RpcPathPlan plan, CancellationToken cancellationToken)
    {
        if (plan.IsFirstHostKnownGood && plan.HostsToRefresh.Count == 0)
            return;

        var checks = new Dictionary<string, Task<bool?>>();
        Task<bool?> Check(string host)
        {
            if (!checks.TryGetValue(host, out var checkTask))
                checks[host] = checkTask = CheckAndRecord(host, cancellationToken);
            return checkTask;
        }

        if (!plan.IsFirstHostKnownGood) {
            var firstTask = Check(plan.FirstHost);
            if (plan.StartNextAfter is { } startNextAfter && plan.HostsToTry.Count > 1) {
                var hedgeTask = Clock.Delay(startNextAfter, cancellationToken);
                await Task.WhenAny(firstTask, hedgeTask).ConfigureAwait(false);
                if (!firstTask.IsCompleted || firstTask.Result != true) {
                    // The first choice is slow or failed, so the next one is tried next to it
                    var nextHost = plan.HostsToTry[1];
                    var nextTask = Check(nextHost);
                    await Task.WhenAny(firstTask, nextTask).ConfigureAwait(false);
                    if (!firstTask.IsCompleted && nextTask is { IsCompleted: true, Result: true })
                        await SwitchTo(nextHost, cancellationToken).ConfigureAwait(false);
                }
            }

            if (await firstTask.ConfigureAwait(false) != true) {
                foreach (var host in plan.HostsToTry.Skip(1))
                    if (await Check(host).ConfigureAwait(false) == true) {
                        await SwitchTo(host, cancellationToken).ConfigureAwait(false);
                        break;
                    }
            }
        }

        foreach (var host in plan.HostsToRefresh) {
            await Check(host).ConfigureAwait(false);
            await PreferVerifiedFirst(cancellationToken).ConfigureAwait(false);
        }
        await PreferVerifiedFirst(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool?> CheckAndRecord(string host, CancellationToken cancellationToken)
    {
        RpcPathMeasurement? measurement;
        try {
            measurement = await Probe.Check(host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            measurement = null;
        }

        // A failure while offline or suspended says nothing about the host
        if (!IsReady)
            return null;

        await Record(host, measurement, cancellationToken).ConfigureAwait(false);
        return measurement is not null;
    }

    private async Task Record(string host, RpcPathMeasurement? measurement, CancellationToken cancellationToken)
    {
        var isOk = measurement is not null;
        var pingTime = measurement?.PingTime ?? TimeSpan.Zero;
        string networkId;
        byte[] data;
        lock (_lock) {
            _history.Record(host, isOk, pingTime.TotalMilliseconds, Clock.UtcNow,
                measurement?.PayloadTime.TotalMilliseconds ?? 0);
            networkId = _networkId;
            data = _history.ToBytes();
        }
        Log?.LogInformation("RPC path check: {Host} {Outcome}, ping {PingTime}",
            host, isOk ? "passed" : "failed", pingTime.ToShortString());
        Updated?.Invoke();
        try {
            await Store.Save(networkId, data, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log?.LogWarning(e, "Failed to save the RPC path history");
        }
    }

    private async Task LoadHistory(CancellationToken cancellationToken)
    {
        var networkId = Volatile.Read(ref _network)?.NetworkId ?? "";
        byte[]? data = null;
        try {
            data = await Store.Load(networkId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log?.LogWarning(e, "Failed to load the RPC path history");
        }

        lock (_lock) {
            _networkId = networkId;
            _history = RpcPathHistory.FromBytesOrEmpty(data ?? []);
        }
    }

    // Moves to the first host of a fresh plan, but only to one that has just been shown to work
    private async Task PreferVerifiedFirst(CancellationToken cancellationToken)
    {
        var plan = Plan(false);
        bool isFreshPass;
        lock (_lock)
            isFreshPass = RpcPathPlanner.IsFreshPass(_history, plan.FirstHost, Clock.UtcNow);
        if (isFreshPass && !string.Equals(plan.FirstHost, Current, StringComparison.OrdinalIgnoreCase))
            await SwitchTo(plan.FirstHost, cancellationToken).ConfigureAwait(false);
    }

    private RpcPathPlan Plan(bool mustVerify)
    {
        lock (_lock)
            return RpcPathPlanner.Plan(Candidates, _history, Clock.UtcNow, mustVerify);
    }

    private void SetCurrent(string host)
    {
        if (Interlocked.Exchange(ref _current, host) == host)
            return;

        Log?.LogWarning("RPC path: {Host}", host);
        CurrentChanged?.Invoke(host);
        Updated?.Invoke();
    }

    private async Task<Signal> WaitForSignal(CancellationToken cancellationToken)
    {
        using var delayTokenSource = cancellationToken.CreateLinkedTokenSource();
        var delayTask = Clock.Delay(RecheckPeriod, delayTokenSource.Token);
        var signalTask = _signal.Task;
        await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        delayTokenSource.Cancel();
        var signal = signalTask.IsCompleted ? signalTask.Result : Signal.Recheck;
        _signal = TaskCompletionSourceExt.New<Signal>();
        return signal;
    }

    private void OnNetworkChanged()
    {
        _signal.TrySetResult(Signal.NetworkChanged);
        try {
            Volatile.Read(ref _cycleTokenSource)?.Cancel();
        }
        catch (ObjectDisposedException) {
            // The cycle has just ended
        }
    }

    // Nested types

    private enum Signal
    {
        Recheck = 0,
        NetworkChanged,
        Failure,
    }
}
