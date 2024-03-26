using ActualChat.Hosting;
using ActualChat.Rpc;
using ActualLab.Rpc;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Mesh;

public sealed class MeshWatcher : WorkerBase
{
    private static readonly TimeSpan DefaultChangeTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DefaultChangeTimeoutIfDev = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultChangeTimeoutIfTested = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<NodeRef, RpcBackendNodePeerRef> _nodePeerRefs = new();
    private readonly ConcurrentDictionary<ShardRef, RpcBackendShardPeerRef> _shardPeerRefs = new();
    private readonly IMutableState<MeshState> _state;

    private IHostApplicationLifetime? HostApplicationLifetime { get; }
    private IMeshLocks NodeLocks { get; }
    private IMomentClock Clock => NodeLocks.Clock;
    private ILogger Log { get; }

    public MeshNode MeshNode { get; }
    public IState<MeshState> State => _state;

    // Settings
    public TimeSpan NodeTimeout { get; init; }

    public MeshWatcher(IServiceProvider services, bool mustStart = true)
    {
        Log = services.LogFor(GetType());
        HostApplicationLifetime = services.GetService<IHostApplicationLifetime>();
        MeshNode = services.MeshNode();
        NodeLocks = services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(NodeLocks));
        _state = services.StateFactory().NewMutable(new MeshState());
        var hostInfo = services.GetRequiredService<HostInfo>();
        NodeTimeout = hostInfo.IsTested ? DefaultChangeTimeoutIfTested
            : hostInfo.IsDevelopmentInstance ? DefaultChangeTimeoutIfDev
            : DefaultChangeTimeout;
        if (mustStart)
            this.Start();
    }

    public RpcPeerRef? GetPeerRef(MeshRef meshRef)
        => !meshRef.ShardRef.IsNone ? GetPeerRef(meshRef.ShardRef)
            : !meshRef.NodeRef.IsNone ? GetPeerRef(meshRef.NodeRef)
                : null;

    public RpcBackendNodePeerRef? GetPeerRef(NodeRef nodeRef)
        => nodeRef.IsNone ? null
            : _nodePeerRefs.GetOrAdd(nodeRef,
                static (nodeRef1, self) => new RpcBackendNodePeerRef(self, nodeRef1),
                this);

    public RpcBackendShardPeerRef? GetPeerRef(ShardRef shardRef)
        => shardRef.IsNone ? null
            : _shardPeerRefs.GetOrAdd(shardRef.Normalize(),
                static (shardRef1, self) => new RpcBackendShardPeerRef(self, shardRef1),
                this).Latest;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var whenLockedSource = new TaskCompletionSource();
        var whenLocked = whenLockedSource.Task;
        _ = Task.Run(() => Announce(whenLockedSource, cancellationToken), CancellationToken.None);

        var state = _state.Value;
        IAsyncSubscription<string>? changes = null;
        var consumeTask = (Task<bool>?)null;
        var failureCount = 0;
        while (true) {
            try {
                if (!whenLocked.IsCompleted)
                    await whenLockedSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                // 1. Subscribe to key space changes unless already subscribed
                changes ??= await NodeLocks.Changes("", cancellationToken).ConfigureAwait(false);

                // 2. Fetch the most current state & update State, if necessary
                var nodes = await ListNodes(cancellationToken).ConfigureAwait(false);
                var diff = nodes.OrderedDiffFrom(state.Nodes);
                if (!diff.IsEmpty) {
                    state = new MeshState(nodes);
                    _state.Value = state;
                    var sb = StringBuilderExt.Acquire();
                    foreach (var item in diff.RemovedItems)
                        sb.Append("- ").Append(item).AppendLine();
                    foreach (var item in diff.AddedItems)
                        sb.Append("+ ").Append(item).AppendLine();
                    sb.Append("= ").Append(state);
                    var description = sb.ToStringAndRelease();
                    // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                    Log.LogInformation($"State changed @ {MeshNode}:{Environment.NewLine}{{Description}}",
                        MeshNode.Ref.Value, description);
                }

                try {
                    consumeTask ??= changes.Reader.WaitToReadAndConsumeAsync(CancellationToken.None);
                    var canRead = await consumeTask
                        .WaitAsync(NodeLocks.UnconditionalCheckPeriod, cancellationToken)
                        .ConfigureAwait(false);
                    // It's important to throw on cancellation here: canRead may return false exactly due to this
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!canRead)
                        throw new OperationCanceledException("Subscription to changes is lost.");
                    consumeTask = null;
                }
                catch (TimeoutException) { }
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
                    MeshNode.Ref.Value, delay.ToShortString());

                await changes.DisposeSilentlyAsync().ConfigureAwait(false);
                changes = null;
                consumeTask = null;
                await Clock.Delay(resumeAt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    protected override Task OnStop()
    {
        Log.LogInformation("Stopped");
        _state.Error = new ObjectDisposedException(GetType().GetName());
        return Task.CompletedTask;
    }

    private async Task<ImmutableArray<MeshNode>> ListNodes(CancellationToken cancellationToken)
    {
        try {
            var keys = await NodeLocks.ListKeys("", cancellationToken).ConfigureAwait(false);
            var ownKey = MeshNode.ToString();
            if (!keys.Contains(ownKey, StringComparer.Ordinal))
                keys.Add(ownKey);

            return keys.Select(key => {
                var node = MeshNode.Parse(key);
                return node == MeshNode ? MeshNode : node;
            }).Order().ToImmutableArray();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            return new[] { MeshNode }.ToImmutableArray();
        }
    }

    private async Task Announce(TaskCompletionSource whenLockedTcs, CancellationToken cancellationToken)
    {
        var key = MeshNode.ToString();
        Log.LogInformation("-> Announce: {MeshNode}", key);

        var cts = cancellationToken.LinkWith(HostApplicationLifetime?.ApplicationStopping ?? CancellationToken.None);
        cancellationToken = cts.Token;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    var holder = await NodeLocks.Lock(key, "", cancellationToken).ConfigureAwait(false);
                    await using var _ = holder.ConfigureAwait(false);
                    whenLockedTcs.TrySetResult();
                    Log.LogInformation("[+] {MeshNode}", key);
                    using var lts = cancellationToken.LinkWith(holder.StopToken);
                    await ActualLab.Async.TaskExt.NeverEndingTask.WaitAsync(lts.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                    // Intended: we keep the lock unless cancellationToken is cancelled
                    Log.LogInformation("[-] {MeshNode} - lost the lock", key);
                }
            }
        }
        finally {
            Log.LogInformation("<- Announce: {MeshNode}", key);
            cts.Dispose();
        }
    }
}
