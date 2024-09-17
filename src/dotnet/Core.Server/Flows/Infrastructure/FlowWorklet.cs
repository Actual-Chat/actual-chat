using ActualLab.Fusion.Internal;

namespace ActualChat.Flows.Infrastructure;

public class FlowWorklet : WorkerBase, IGenericTimeoutHandler
{
    protected readonly TaskCompletionSource WhenReadySource = new();
    protected Channel<QueueEntry> Queue { get; init; }
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
        Writer = Queue.Writer;
        StopToken.Register(() => Writer.TryComplete());
    }

    void IGenericTimeoutHandler.OnTimeout()
        => Dispose();

    public override string ToString()
        => $"{GetType().Name}('{FlowId}')";

    // The `long` it returns is DbFlow/FlowData.Version
    public Task<long> ProcessEvent(IFlowEvent evt, CancellationToken cancellationToken)
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
        var flow = await Host.Flows.GetOrStart(FlowId, cancellationToken).ConfigureAwait(false);
        flow = flow.Clone();
        flow.Initialize(flow.Id, flow.Version, flow.Step, flow.HardResumeAt, this);
        if (flow.Step == FlowSteps.Starting) {
            var entry = new QueueEntry(new FlowStartEvent(FlowId), cancellationToken);
            if (!Writer.TryWrite(entry))
                return; // Already stopped
        }
        WhenReadySource.TrySetResult();

        var clock = Timeouts.Generic.Clock;
        var reader = Queue.Reader;
        do {
            // We don't pass cancellationToken to WaitToReadAsync, coz
            // the channel is reliably getting closed on dispose -
            // see DisposeAsyncCore and IGenericTimeoutHandler.OnTimeout.
            var canReadTask = reader.WaitToReadAsync(CancellationToken.None);
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
            if (!reader.TryRead(out var entry))
                continue;

            flow = await HandleEvent(flow, entry, cancellationToken).ConfigureAwait(false);
        } while (flow.Step != FlowSteps.Removed);
    }

    protected override async Task OnStop()
    {
        WhenReadySource.TrySetCanceled(StopToken);
        await foreach (var entry in Queue.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            entry.ResultSource.TrySetCanceled(StopToken);
        Shard.Worklets.TryRemove(FlowId, this);
    }

    // Private methods

    private async Task<Flow> HandleEvent(Flow flow, QueueEntry entry, CancellationToken cancellationToken)
    {
        // This method should never throw!
        if (entry.CancellationToken.IsCancellationRequested) {
            // Entry is cancelled by the moment we dequeue it
            entry.ResultSource.TrySetCanceled(entry.CancellationToken);
            return flow;
        }
        if (cancellationToken.IsCancellationRequested) {
            // The worklet is already terminating
            entry.ResultSource.TrySetCanceled(cancellationToken);
            return flow;
        }

        var backup = flow.Clone();
        try {
            var evt = entry.Event;
            while (true) {
                var transition = await flow.HandleEvent(evt, cancellationToken).ConfigureAwait(false);
                if (transition.IsNone)
                    break; // No transition
                if (transition.HardResumeAt.HasValue)
                    break; // Transition implies waiting for event

                evt = new FlowResumeEvent(flow.Id, false, transition.Tag);
            }
            entry.ResultSource.TrySetResult(flow.Version);
        }
        catch (Exception e) {
            flow = backup;
            if (e.IsCancellationOf(cancellationToken))
                entry.ResultSource.TrySetCanceled(cancellationToken);
            else {
                entry.ResultSource.TrySetException(e);
                Log.LogError(e, "`{Id}` @ {NextStep} failed", flow.Id, flow.Step);
            }
        }
        return flow;
    }

    // Nested types

    public readonly record struct QueueEntry(
        IFlowEvent Event,
        CancellationToken CancellationToken)
    {
        public TaskCompletionSource<long> ResultSource { get; init; } = new ();
    }
}
