namespace ActualChat.Mesh;

public sealed class MeshWatcher : WorkerBase
{
    private readonly IMutableState<MeshState> _state;
    private IMeshLocks<MeshState> MeshLocks { get; }
    private ILogger Log { get; }

    public MeshNode ThisNode { get; }
    public IState<MeshState> State => _state;
    public IMomentClock Clock => MeshLocks.Clock;

    public MeshWatcher(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        ThisNode = services.MeshNode();
        MeshLocks = services.MeshLocks<MeshState>();
        _state = services.StateFactory().NewMutable(new MeshState());
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        Log.LogInformation("Started");
        var whenLockedTcs = new TaskCompletionSource();
        var whenLocked = whenLockedTcs.Task;
        _ = Task.Run(() => KeepLocked(whenLockedTcs, cancellationToken), CancellationToken.None);

        var state = _state.Value;
        IAsyncSubscription<string>? changes = null;
        var consumeTask = (Task<bool>?)null;
        var failureCount = 0;
        while (true) {
            try {
                if (!whenLocked.IsCompleted)
                    await whenLockedTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                // 1. Subscribe to key space changes unless already subscribed
                changes ??= await MeshLocks.Changes("", cancellationToken).ConfigureAwait(false);

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
                    Log.LogInformation($"State changed:{Environment.NewLine}{{Description}}", description);
                }

                try {
                    consumeTask ??= changes.Reader.WaitToReadAndConsumeAsync(cancellationToken);
                    // It's fine to use CancellationToken.None here:
                    // consumeTask already depends on cancellationToken.
                    var canRead = await consumeTask
                        .WaitAsync(MeshLocks.UnconditionalCheckPeriod, CancellationToken.None)
                        .ConfigureAwait(false);
                    // It's important to throw on cancellation here: canRead may return false exactly due to this
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!canRead) {
                        await changes.DisposeSilentlyAsync().ConfigureAwait(false);
                        changes = null;
                        consumeTask = null;
                        continue;
                    }
                    consumeTask = null;
                }
                catch (TimeoutException) { }
                failureCount = 0;
            }
            catch (Exception e) {
                if (e.IsCancellationOf(cancellationToken)) {
                    await changes.DisposeSilentlyAsync().ConfigureAwait(false);
                    throw;
                }

                var delay = MeshLocks.RetryDelays[++failureCount];
                var resumeAt = Clock.Now + delay;
                Log.LogError(e, "State update cycle failed, will retry in {Delay}", delay.ToShortString());

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
        var keys = await MeshLocks.ListKeys("", cancellationToken).ConfigureAwait(false);
        return keys.Select(MeshNode.Parse).Order().ToImmutableArray();
    }

    private async Task KeepLocked(TaskCompletionSource whenLockedTcs, CancellationToken cancellationToken)
    {
        var key = ThisNode.ToString();
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var holder = await MeshLocks
                    .Lock(key, "", cancellationToken)
                    .ConfigureAwait(false);
                await using var _ = holder.ConfigureAwait(false);
                whenLockedTcs.TrySetResult();
                using var lts = cancellationToken.LinkWith(holder.StopToken);
                await ActualLab.Async.TaskExt.NeverEndingTask.WaitAsync(lts.Token).SilentAwait();
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                // Intended: we keep the lock unless cancellationToken is cancelled
            }
        }
    }
}
