using ActualChat.Hosting;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Mesh;

public sealed class MeshWatcher : WorkerBase
{
    private static readonly TimeSpan DefaultNodeTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DefaultNodeTimeoutIfDev = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultNodeTimeoutIfTested = TimeSpan.FromSeconds(2);

    private readonly MutableState<MeshState> _state;

    private IHostApplicationLifetime? HostApplicationLifetime { get; }
    private IMeshLocks NodeLocks { get; }
    private MomentClock Clock => NodeLocks.Clock;
    private ILogger Log { get; }

    public MeshNode OwnNode { get; }
    public IState<MeshState> State => _state;

    // Settings
    public TimeSpan NodeTimeout { get; init; }

    public MeshWatcher(IServiceProvider services, bool mustStart = true)
    {
        Log = services.LogFor(GetType());
        HostApplicationLifetime = services.GetService<IHostApplicationLifetime>();
        OwnNode = services.GetRequiredService<MeshNode>();
        NodeLocks = services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(NodeLocks));
        _state = services.StateFactory().NewMutable(new MeshState());
        var hostInfo = services.GetRequiredService<HostInfo>();
        NodeTimeout = hostInfo.IsTested ? DefaultNodeTimeoutIfTested
            : hostInfo.IsDevelopmentInstance ? DefaultNodeTimeoutIfDev
            : DefaultNodeTimeout;
        if (mustStart)
            this.Start();
    }

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
                    var sb = ActualLab.Text.StringBuilderExt.Acquire();
                    foreach (var item in diff.RemovedItems)
                        sb.Append("- ").Append(item).AppendLine();
                    foreach (var item in diff.AddedItems)
                        sb.Append("+ ").Append(item).AppendLine();
                    sb.Append("= ").Append(state);
                    var description = sb.ToStringAndRelease();
                    // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                    Log.LogInformation($"State @ {OwnNode}:{Environment.NewLine}{{Description}}",
                        OwnNode.Ref.Value, description);
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
                    OwnNode.Ref.Value, delay.ToShortString());

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
            var ownKey = OwnNode.ToString();
            if (!keys.Contains(ownKey, StringComparer.Ordinal))
                keys.Add(ownKey);

            return [
                ..keys.Select(key => {
                    var node = MeshNode.Parse(key);
                    return node == OwnNode ? OwnNode : node;
                }).Order(),
            ];
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            return [..new[] { OwnNode }];
        }
    }

    private async Task Announce(TaskCompletionSource whenLockedTcs, CancellationToken cancellationToken)
    {
        var key = OwnNode.ToString();
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
                    await ActualLab.Async.TaskExt.NewNeverEndingUnreferenced()
                        .WaitAsync(lts.Token)
                        .ConfigureAwait(false);
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
