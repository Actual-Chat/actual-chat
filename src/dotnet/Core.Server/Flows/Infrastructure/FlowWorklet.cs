using ActualLab.Fusion.Internal;

namespace ActualChat.Flows.Infrastructure;

public class FlowWorklet : WorkerBase, IGenericTimeoutHandler
{
    protected readonly TaskCompletionSource WhenReadySource = new();
    protected Channel<QueueEntry> Queue { get; init; }
    protected ChannelReader<QueueEntry> Reader { get; init; }
    protected ChannelWriter<QueueEntry> Writer { get; init; }

    public FlowHostShard Shard { get; }
    public FlowHost Host => Shard.Host;
    public FlowId FlowId { get; }
    public ILogger Log { get; }

    public FlowWorklet(FlowHostShard shard, FlowId flowId)
        : base(shard.StopToken.CreateLinkedTokenSource())
    {
        Shard = shard;
        FlowId = flowId;
        var flowType = Host.Registry.TypeByName[flowId.Name];
        Log = Host.Services.LogFor(flowType);

        Queue = Channel.CreateUnbounded<QueueEntry>(new() {
            SingleReader = true,
            SingleWriter = false,
        });
        Reader = Queue.Reader;
        Writer = Queue.Writer;
        StopToken.Register(() => Writer.TryComplete());
    }

    void IGenericTimeoutHandler.OnTimeout()
        => Dispose();

    public override string ToString()
        => $"{GetType().Name}('{FlowId}')";

    // The `long` it returns is DbFlow/FlowData.Version
    public Task<long> EnqueueAndProcessEvent(IFlowEvent evt, CancellationToken cancellationToken)
    {
        var whenReady = WhenReadySource.Task;
        if (!whenReady.IsCompleted)
            return ProcessEventAsync();

        var entry = new QueueEntry(evt, cancellationToken);
        if (Writer.TryWrite(entry))
            return entry.ResultSource.Task;

        StopToken.ThrowIfCancellationRequested(); // Should always throw here
        throw new OperationCanceledException();

        async Task<long> ProcessEventAsync()
        {
            await whenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            var entry1 = new QueueEntry(evt, cancellationToken);
            if (Writer.TryWrite(entry1))
                return await entry1.ResultSource.Task.ConfigureAwait(false);

            StopToken.ThrowIfCancellationRequested(); // Should always throw here
            throw new OperationCanceledException();
        }
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var clock = Timeouts.Generic.Clock;
        var failureDelays = Flow.Defaults.FailureDelays;
        var failureCount = 0;
        while (true) {
            Flow? flow = null;
            try {
                flow = await Host.Flows.GetOrStart(FlowId, cancellationToken).ConfigureAwait(false);
                flow = flow.Clone();
                failureDelays = flow.FailureDelays;
                flow.Initialize(flow.Id, flow.Version, flow.Step, flow.HardResumeAt, this);
                if (flow.Step == FlowSteps.Starting) {
                    var entry = new QueueEntry(new FlowStartEvent(FlowId), cancellationToken);
                    if (!Writer.TryWrite(entry))
                        return; // Already stopped
                }
                WhenReadySource.TrySetResult();

                do {
                    // We don't pass cancellationToken to WaitToReadAsync, coz
                    // the channel is reliably getting closed on dispose -
                    // see DisposeAsyncCore and IGenericTimeoutHandler.OnTimeout.
                    var canReadTask = Reader.WaitToReadAsync(CancellationToken.None);
                    if (canReadTask.IsCompleted) {
                        if (!await canReadTask.ConfigureAwait(false))
                            return;
                    }
                    else {
                        // Enlist this worklet for timeout-based removal
                        Timeouts.Generic.AddOrUpdateToLater(this, clock.Now + flow.KeepAliveFor);
                        if (!await canReadTask.ConfigureAwait(false))
                            return;

                        // Un-enlist this worklet for timeout-based removal
                        Timeouts.Generic.Remove(this);
                    }
                    if (!Reader.TryRead(out var entry))
                        continue;

                    await ProcessEvent(flow, entry, cancellationToken).ConfigureAwait(false);
                    failureCount = 0;
                } while (flow.Step != FlowSteps.Removed);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                var delay = failureDelays[++failureCount];
                Log.LogError(e,
                    "`{Id}` @ {NextStep} failed (#{FailureCount}), will resume in {Delay}",
                    FlowId, flow?.Step ?? "n/a", failureCount, delay);
                await clock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    protected override async Task OnStop()
    {
        WhenReadySource.TrySetCanceled(StopToken);
        await foreach (var entry in Queue.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            entry.ResultSource.TrySetCanceled(StopToken);
        Shard.Worklets.TryRemove(FlowId, this);
    }

    // Private methods

    private async Task ProcessEvent(Flow flow, QueueEntry entry, CancellationToken cancellationToken)
    {
        // This method should never throw!
        if (entry.CancellationToken.IsCancellationRequested) {
            // Entry is cancelled by the moment we dequeue it
            entry.ResultSource.TrySetCanceled(entry.CancellationToken);
            return;
        }
        if (cancellationToken.IsCancellationRequested) {
            // The worklet is already terminating
            entry.ResultSource.TrySetCanceled(cancellationToken);
            return;
        }

        try {
            var transition = await flow.ProcessEvent(entry.Event, cancellationToken).ConfigureAwait(false);
            entry.ResultSource.TrySetResult(flow.Version);
            if (transition is { IsNone: false, HardResumeAt: null }) {
                entry = new QueueEntry(new FlowResumeEvent(flow.Id, false, transition.Tag), CancellationToken.None);
                await Writer.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            entry.ResultSource.TrySetException(e);
            throw;
        }
    }

    // Nested types

    public readonly record struct QueueEntry(
        IFlowEvent Event,
        CancellationToken CancellationToken
        ) : ICanBeNone<QueueEntry>
    {
        public static QueueEntry None => default;

        public bool IsNone => Event == null;
        public TaskCompletionSource<long> ResultSource { get; init; } = new ();
    }
}
