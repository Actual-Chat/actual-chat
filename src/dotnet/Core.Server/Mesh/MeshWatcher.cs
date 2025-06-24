using ActualChat.Hosting;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Mesh;

public sealed class MeshWatcher : WorkerBase, IHasServices
{
    private static readonly TimeSpan DefaultNodeTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultNodeTimeoutIfTest = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultNodeTimeoutIfDebug = TimeSpan.FromMinutes(10);

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
    public TimeSpan NodeTimeout { get; init; }
    public bool MustStopHostOnAnnounceFailure { get; init; }
#if !DEBUG
        = true;
#endif

    public MeshWatcher(IServiceProvider services, bool mustStart = true)
        : base(services.HostLifetimeIfExist()?.ApplicationStopping.CreateLinkedTokenSource())
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
                InitialValue = new MeshState([ThisNode], ImmutableDictionary<NodeRef, CpuTimestamp>.Empty, [ThisNode]),
                UpdateDelayer = FixedDelayer.YieldUnsafe,
                Category = StateCategories.Get(GetType(), nameof(State)),
            },
            ComputeState);
        var hostInfo = services.GetRequiredService<HostInfo>();
        NodeTimeout = hostInfo.IsTested
            ? DefaultNodeTimeoutIfTest
#if DEBUG
            : DefaultNodeTimeoutIfDebug;
#else
            : DefaultNodeTimeout;
#endif
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
        if (StopToken.IsCancellationRequested)
            return lastState.ToFinal();

        var dyingNodes = lastState.DyingNodes;
        // Updating online and dying nodes
        var onlineNodes = await _onlineNodes.Use(cancellationToken).ConfigureAwait(false);
        var now = CpuTimestamp.Now;
        var diff = onlineNodes.OrderedDiffFrom(lastState.OnlineNodes);
        if (!diff.IsEmpty) {
            foreach (var item in diff.RemovedItems)
                if (!dyingNodes.ContainsKey(item.Ref))
                    dyingNodes = dyingNodes.Add(item.Ref, now + NodeTimeout);
            foreach (var item in diff.AddedItems)
                if (dyingNodes.TryGetValue(item.Ref, out var dyingAt) && dyingAt > now)
                    dyingNodes = dyingNodes.Remove(item.Ref);
        }

        // Computing nodes
        var nodes = lastState.Nodes
            .Concat(onlineNodes)
            .Distinct()
            .Where(x => !dyingNodes.TryGetValue(x.Ref, out var dyingAt) || dyingAt > now)
            .Order()
            .ToImmutableArray();

        // Composing the final state
        var result = new MeshState(onlineNodes, dyingNodes, nodes);
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        Log.LogInformation(
            $"{nameof(ComputeState)} @ {{ThisNode}}:{Environment.NewLine}{{State}}",
            ThisNode.Ref.Value, result);

        var minInvalidateIn = TimeSpan.MaxValue;
        foreach (var dyingAt in dyingNodes.Values) {
            var invalidateIn = dyingAt - now;
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
                    foreach (var item in diff.RemovedItems)
                        sb.Append("- ").Append(item).AppendLine();
                    foreach (var item in diff.AddedItems)
                        sb.Append("+ ").Append(item).AppendLine();
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
            var ownKey = ThisNode.ToString();
            if (!keys.Contains(ownKey, StringComparer.Ordinal))
                keys.Add(ownKey);

            return [
                ..keys.Select(key => {
                    var node = MeshNode.Parse(key);
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
        var key = ThisNode.ToString();
        Log.LogInformation("-> Announce: {MeshNode}", key);

        try {
            var holderStopToken = CancellationToken.None;
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    var holder = await NodeLocks.Lock(key, "", cancellationToken).ConfigureAwait(false);
                    await using var _1 = holder.ConfigureAwait(false);

                    holderStopToken = holder.StopToken;
                    _whenAnnouncedSource.TrySetResult();
                    Log.LogInformation("[+] {MeshNode}", key);

                    if (MustStopHostOnAnnounceFailure)
                        holder.StopToken.Register(() => {
                            if (cancellationToken.IsCancellationRequested)
                                return;
                            var hostLifetime = Services.GetService<IHostApplicationLifetime>();
                            if (hostLifetime is null || hostLifetime.ApplicationStopping.IsCancellationRequested)
                                return;

                            Log.LogCritical("[!] {MeshNode} - lost the lock, stopping the host", key);
                            hostLifetime.StopApplication();
                        });

                    using var linkedTokenSource = cancellationToken.LinkWith(holderStopToken);
                    using var dTask = linkedTokenSource.Token.ToTask();
                    await dTask.Resource.ConfigureAwait(false);
                }
                catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                    Log.LogWarning(e,
                        "[!] {MeshNode} - failed to acquire the lock, holder.StopToken.IsCancellationRequested = {StopTokenIsCancelled}",
                        key, holderStopToken.IsCancellationRequested);
                    // Intended
                }
                Log.LogInformation("[-] {MeshNode} - lost the lock", key);
            }
        }
        finally {
            Log.LogInformation("<- Announce: {MeshNode}", key);
        }
    }
}
