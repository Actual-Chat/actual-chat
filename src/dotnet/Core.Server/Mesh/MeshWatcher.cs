namespace ActualChat.Mesh;

public sealed class MeshWatcher : WorkerBase, IHasServices
{
    private readonly AsyncTaskMethodBuilder _whenAnnouncedSource = AsyncTaskMethodBuilderExt.New();
    private readonly MutableState<ImmutableArray<MeshNode>> _onlineNodes;
    private readonly ComputedState<MeshState> _state;
    private ImmutableArray<MeshNode> _lastNodes;
    private int _listNodesFailureCount;

    private IMeshLocks EndpointLocks { get; }
    private IMeshLocks NodeLocks { get; }
    private MomentClock Clock => NodeLocks.Clock;
    private ILogger Log { get; }

    public IServiceProvider Services { get; }
    public MeshNode ThisNode { get; }
    public IState<MeshState> State => _state;
    public Task WhenAnnounced => _whenAnnouncedSource.Task;

    public MeshWatcher(IServiceProvider services, bool mustStart = true)
        : base(services.HostLifetimeIfExist().CreateStopTokenSource())
    {
        Services = services;
        Log = services.LogFor(GetType());
        ThisNode = services.GetRequiredService<MeshNode>();

        var meshLocks = services.MeshLocks();
        var lockOptions = meshLocks.LockOptions;
        if (ReferenceEquals(lockOptions, MeshLockOptions.Release))
            lockOptions = MeshLockOptions.ReleaseMeshWatcher;
        var baseLocks = meshLocks.WithLockOptions(lockOptions);
        EndpointLocks = baseLocks.WithKeyPrefix(nameof(EndpointLocks));
        NodeLocks = baseLocks.WithKeyPrefix(nameof(NodeLocks));

        var stateFactory = services.StateFactory();
        _lastNodes = [ThisNode];
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

        var onlineNodes = (await _onlineNodes.Use(cancellationToken).ConfigureAwait(false)).ToDictionary(x => x.Ref);
        var allNodes = onlineNodes.ToImmutableDictionary();

        // Composing the final state
        var result = new MeshState(Clock, allNodes);
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        Log.LogInformation(
            $"{nameof(ComputeState)} @ {{ThisNode}}:{Environment.NewLine}{{State}}",
            ThisNode.Ref.Value, result);
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
            if (!keys.Contains(ownKey))
                keys.Add(ownKey);

            var nodes = ImmutableArray.CreateRange(
                keys.Select(key => {
                    var node = MeshNode.FromLockKey(key);
                    return node == ThisNode ? ThisNode : node;
                }).Where(node => {
                    if (node.Endpoint != ThisNode.Endpoint || node == ThisNode)
                        return true;

                    Log.LogError(
                        "Filtering out stale mesh node with same endpoint, this should never happen: {StaleNode} (current: {ThisNode})",
                        node, ThisNode);
                    return false;
                }).Order());
            _lastNodes = nodes; // This method isn't called concurrently, so it's safe to update
            _listNodesFailureCount = 0;
            return nodes;
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            // Serving the last known list keeps the mesh going, but a persistent failure
            // means the topology is frozen - it must be visible in logs
            _listNodesFailureCount++;
            Log.LogError(e, "ListNodes failed ({FailureCount} in a row) @ {MeshNode} - using the last known node list",
                _listNodesFailureCount, ThisNode.Ref.Value);
            return _lastNodes;
        }
    }

    private async Task Announce(CancellationToken cancellationToken)
    {
        var endpointLockKey = ThisNode.Endpoint;
        var nodeLockKey = ThisNode.LockKey;
        Log.LogInformation("-> Announce: {MeshNode}", nodeLockKey);

        try {
            // First, acquire the endpoint lock to ensure only one server per endpoint can announce
            var endpointHolder = await EndpointLocks
                .Lock(endpointLockKey, cancellationToken)
                .ConfigureAwait(false);
            await using var _1 = endpointHolder.ConfigureAwait(false);
            Log.LogInformation("[+] Endpoint lock acquired: {Endpoint}", endpointLockKey);

            // Then, acquire the node lock
            var nodeHolder = await NodeLocks
                .Lock(nodeLockKey, cancellationToken)
                .ConfigureAwait(false);
            await using var _2 = nodeHolder.ConfigureAwait(false);

            _whenAnnouncedSource.TrySetResult();
            Log.LogInformation("[+] {MeshNode}", nodeLockKey);

            // Wait until either lock is lost or cancellation is requested
            using var linkedTokenSource = cancellationToken
                .LinkWith(endpointHolder.StopToken)
                .Token.LinkWith(nodeHolder.StopToken);
            await TaskExt.NeverEnding(linkedTokenSource.Token).ConfigureAwait(false);
            // Can't reach this point: above await can only complete with cancellation
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogCritical(e, "[!] {MeshNode} - announcement lock lost, stopping the host", nodeLockKey);
            if (Services.HostLifetimeIfExist() is { } hostLifetime
                && !hostLifetime.StopToken().IsCancellationRequested)
                hostLifetime.StopApplication();
            throw;
        }
        finally {
            Log.LogInformation("<- Announce: {MeshNode}", nodeLockKey);
        }
    }
}
