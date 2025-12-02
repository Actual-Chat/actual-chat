using ActualChat.Hosting;

namespace ActualChat.Mesh;

public sealed class MeshWatcher : WorkerBase, IHasServices
{
    private static readonly TimeSpan DefaultOfflineTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultOfflineTimeoutIfTested = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultOfflineTimeoutIfDebugged = TimeSpan.FromMinutes(10);

    private readonly AsyncTaskMethodBuilder _whenAnnouncedSource = AsyncTaskMethodBuilderExt.New();
    private readonly MutableState<ImmutableArray<MeshNode>> _onlineNodes;
    private readonly ComputedState<MeshState> _state;

    private IMeshLocks NodeLocks { get; }
    private MomentClock Clock => NodeLocks.Clock;
    private ILogger Log { get; }

    public IServiceProvider Services { get; }
    public MeshNode ThisNode { get; }
    public IState<MeshState> State => _state;
    public Task WhenAnnounced => _whenAnnouncedSource.Task;

    // Settings
    public TimeSpan OfflineTimeout { get; init; } // Turns dead after this
    public TimeSpan RelockTimeout { get; init; }
    public bool MustStopHostOnAnnounceFailure { get; init; }
#if !DEBUG
        = true;
#endif

    public MeshWatcher(IServiceProvider services, bool mustStart = true)
        : base(services.HostLifetimeIfExist().CreateStopTokenSource())
    {
        Services = services;
        Log = services.LogFor(GetType());
        ThisNode = services.GetRequiredService<MeshNode>();
        NodeLocks = services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(NodeLocks));
        var stateFactory = services.StateFactory();
        _onlineNodes = stateFactory.NewMutable<ImmutableArray<MeshNode>>(
            new () {
                InitialValue = [ThisNode],
                Category = StateCategories.Get(GetType(), nameof(_onlineNodes)),
            });
        _state = stateFactory.NewComputed<MeshState>(
            new () {
                InitialValue = new MeshState(Clock, ImmutableDictionary<NodeRef, MeshNode>.Empty.Add(ThisNode.Ref, ThisNode)),
                UpdateDelayer = FixedDelayer.YieldUnsafe,
                Category = StateCategories.Get(GetType(), nameof(State)),
            },
            ComputeState);
        var hostInfo = services.GetRequiredService<HostInfo>();
        OfflineTimeout = hostInfo.IsTested
            ? DefaultOfflineTimeoutIfTested
#if DEBUG
            : Debugger.IsAttached ? DefaultOfflineTimeoutIfDebugged : DefaultOfflineTimeout;
#else
            : DefaultOfflineTimeout;
#endif
        RelockTimeout = (OfflineTimeout - 2*NodeLocks.LockOptions.ExpirationPeriod).Positive();
        if (mustStart)
            this.Start();
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var announceTask = Announce(cancellationToken);
        var updateOnlineNodesTask = UpdateOnlineNodes(cancellationToken);
        return Task.WhenAll(announceTask, updateOnlineNodesTask);
    }

    protected override async Task OnStop()
    {
        Log.LogInformation("Stopping");
        // Final State.Value must throw OperationCanceledException
        var computed = (Computed)_state.Computed;
        if (computed.IsInvalidated())
            computed = await computed.UpdateUntyped().ConfigureAwait(false);
        computed.Invalidate(true);
        await computed.UpdateUntyped().SilentAwait(false); // Triggering the final update
        _state.Dispose();
    }

    // Private methods

    private async Task<MeshState> ComputeState(ComputedState<MeshState> state, CancellationToken cancellationToken)
    {
        var lastState = state.LastNonErrorValue;
        if (lastState.IsFinal || StopToken.IsCancellationRequested)
            return lastState.ToFinal();

        var now = Clock.Now;
        var lastAllNodes = lastState.AllNodes;
        var onlineNodes = (await _onlineNodes.Use(cancellationToken).ConfigureAwait(false)).ToDictionary(x => x.Ref);
        var newOnlineNodes = onlineNodes.Values.Where(x => !lastAllNodes.ContainsKey(x.Ref)).ToList();
        var allNodes = lastAllNodes.Values.Select(node => {
            var isOnline = onlineNodes.ContainsKey(node.Ref);
            switch (node.State) {
            case MeshNodeState.Online:
                return isOnline
                    ? node
                    : node.ToOffline(now + OfflineTimeout);
            case MeshNodeState.Offline:
                return isOnline
                    ? node.ToOnline()
                    : node.DeadAt < now ? node.ToDead() : node;
            case MeshNodeState.Dead:
                return node;
            default:
                throw StandardError.Internal("Unexpected MeshNode.State: " + node.State);
            }
        }).Concat(newOnlineNodes).ToImmutableDictionary(x => x.Ref);
        allNodes = allNodes.AddRange(onlineNodes.Where(kv => !lastAllNodes.ContainsKey(kv.Key)));

        // Composing the final state
        var result = new MeshState(Clock, allNodes);
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        Log.LogInformation(
            $"{nameof(ComputeState)} @ {{ThisNode}}:{Environment.NewLine}{{State}}",
            ThisNode.Ref.Value, result);

        var deadNodes = allNodes.Values.Where(x => x.State is MeshNodeState.Offline);
        var minInvalidateIn = TimeSpan.MaxValue;
        foreach (var deadAt in deadNodes.Select(x => x.DeadAt!.Value)) {
            var invalidateIn = deadAt - now;
            if (invalidateIn <= TimeSpan.Zero)
                invalidateIn = TimeSpan.MaxValue;
            minInvalidateIn = TimeSpanExt.Min(minInvalidateIn, invalidateIn);
        }
        if (minInvalidateIn != TimeSpan.MaxValue) {
            Computed.GetCurrent().Invalidate(minInvalidateIn);
            Log.LogInformation(
                $"{nameof(ComputeState)} @ {{ThisNode}}: will auto-invalidate in {{InvalidateIn}}",
                ThisNode.Ref.Value, minInvalidateIn.ToShortString());
        }
        return result;
    }

    private async Task UpdateOnlineNodes(CancellationToken cancellationToken)
    {
        IAsyncSubscription<string>? changes = null;
        var consumeTask = (Task<bool>?)null;
        var failureCount = 0;
        while (true) {
            try {
                if (!WhenAnnounced.IsCompleted)
                    await WhenAnnounced.WaitAsync(cancellationToken).ConfigureAwait(false);

                // 1. Subscribe to key space changes unless already subscribed
                changes ??= await NodeLocks.Changes("", cancellationToken).ConfigureAwait(false);

                // 2. Fetch the most current state & update State, if necessary
                var nodes = await ListNodes(cancellationToken).ConfigureAwait(false);
                var diff = nodes.OrderedDiffFrom(_onlineNodes.Value);
                if (!diff.IsEmpty) {
                    var sb = ActualLab.Text.StringBuilderExt.Acquire();
                    foreach (var node in diff.RemovedItems)
                        sb.Append("- ").Append(node).AppendLine();
                    foreach (var node in diff.AddedItems)
                        sb.Append("+ ").Append(node).AppendLine();
                    sb.Append("= ").Append(nodes.Select(x => x.Ref).ToDelimitedString());
                    // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                    Log.LogInformation(
                        $"{nameof(UpdateOnlineNodes)} @ {{ThisNode}}:{Environment.NewLine}{{Description}}",
                        ThisNode.Ref.Value, sb.ToStringAndRelease());

                    // Exposing the value after logging the diff
                    _onlineNodes.Value = nodes;
                }

                try {
                    consumeTask ??= changes.Reader.WaitToReadAndConsumeAsync(CancellationToken.None);
                    var canReadResult = await consumeTask
                        .WaitResultAsync(NodeLocks.LockOptions.UnconditionalCheckPeriod, cancellationToken)
                        .ConfigureAwait(false);
                    if (canReadResult.IsValue(out var canRead, out var error)) {
                        // It's important to throw on cancellation here: canRead may return false exactly due to this
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!canRead)
                            throw new OperationCanceledException("Subscription to changes is lost.");
                        consumeTask = null;
                    }
                    else if (error is not TimeoutException) {
                        canReadResult.ThrowIfError();
                    }
                }
                catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                    await changes.DisposeSilentlyAsync().ConfigureAwait(false);
                    changes = null;
                    consumeTask = null;
                    continue;
                }
                failureCount = 0;
            }
            catch (Exception e) {
                if (e.IsCancellationOf(cancellationToken)) {
                    await changes.DisposeSilentlyAsync().ConfigureAwait(false);
                    throw;
                }

                var delay = NodeLocks.RetryDelays[++failureCount];
                var resumeAt = Clock.Now + delay;
                Log.LogError(e, "State update cycle failed @ {MeshNode}, will retry in {Delay}",
                    ThisNode.Ref.Value, delay.ToShortString());

                await changes.DisposeSilentlyAsync().ConfigureAwait(false);
                changes = null;
                consumeTask = null;
                await Clock.Delay(resumeAt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<ImmutableArray<MeshNode>> ListNodes(CancellationToken cancellationToken)
    {
        try {
            var keys = await NodeLocks.ListKeys("", cancellationToken).ConfigureAwait(false);
            var ownKey = ThisNode.LockKey;
            if (!keys.Contains(ownKey, StringComparer.Ordinal))
                keys.Add(ownKey);

            return [
                ..keys.Select(key => {
                    var node = MeshNode.FromLockKey(key);
                    return node == ThisNode ? ThisNode : node;
                }).Order(),
            ];
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            return [ThisNode];
        }
    }

    private async Task Announce(CancellationToken cancellationToken)
    {
        var lockKey = ThisNode.LockKey;
        var timeout = (Moment?)null;
        Log.LogInformation("-> Announce: {MeshNode}", lockKey);

        void CheckTimeout() {
            if (!timeout.HasValue || Clock.Now < timeout.GetValueOrDefault())
                return;

            Log.LogCritical("[!] {MeshNode} - failed to (re)acquire the node announcement lock in time", lockKey);
            if (MustStopHostOnAnnounceFailure
                && Services.HostLifetimeIfExist() is { } hostLifetime
                && !hostLifetime.StopToken().IsCancellationRequested) {
                Log.LogCritical("[!] {MeshNode} - stopping the host", lockKey);
                hostLifetime.StopApplication();
            }
            throw StandardError.Timeout("Failed to (re)acquire the node announcement lock in time.");
        }

        try {
            var holderStopToken = CancellationToken.None;
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    CheckTimeout();
                    var holder = await NodeLocks
                        .Lock(lockKey, cancellationToken)
                        .ConfigureAwait(false);
                    await using var _1 = holder.ConfigureAwait(false);
                    CheckTimeout();
                    timeout = null;

                    holderStopToken = holder.StopToken;
                    _whenAnnouncedSource.TrySetResult();
                    Log.LogInformation("[+] {MeshNode}", lockKey);

                    using var linkedTokenSource = cancellationToken.LinkWith(holderStopToken);
                    await TaskExt.NeverEnding(linkedTokenSource.Token).ConfigureAwait(false);
                    // Can't reach this point: above await can only complete with cancellation
                }
                catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                    if (e is TimeoutException)
                        throw; // Thrown by CheckTimeout()

                    Log.LogError(e,
                        "[!] {MeshNode} - failed to (re)acquire the node announcement; holder.StopToken.IsCancellationRequested = {StopTokenIsCancelled}",
                        lockKey, holderStopToken.IsCancellationRequested);
                }
                timeout ??= Clock.Now + RelockTimeout;
            }
        }
        finally {
            Log.LogInformation("<- Announce: {MeshNode}", lockKey);
        }
    }
}
