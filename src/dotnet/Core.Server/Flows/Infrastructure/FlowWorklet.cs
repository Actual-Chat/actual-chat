using ActualLab.Fusion.Internal;

namespace ActualChat.Flows.Infrastructure;

public class FlowWorklet : WorkerBase, IGenericTimeoutHandler
{
    protected static readonly ChannelClosedException ChannelClosedExceptionInstance = new();

    protected Channel<QueueEntry> Queue { get; set; }
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
        var flowType = Host.Registry.Types[flowId.Name];
        Log = Host.Services.LogFor(flowType);

        Queue = Channel.CreateUnbounded<QueueEntry>(new() {
            SingleReader = true,
            SingleWriter = true,
        });
        Writer = Queue.Writer;
    }

    protected override Task DisposeAsyncCore()
    {
        // DisposeAsyncCore always runs inside lock (Lock)
        Writer.TryComplete();
        return base.DisposeAsyncCore();
    }

    public override string ToString()
        => $"{GetType().Name}('{FlowId}')";

    // The `long` it returns is DbFlow/FlowData.Version
    public Task<long> HandleEvent(object? evt, CancellationToken cancellationToken)
    {
        var entry = new QueueEntry(evt, cancellationToken);
        bool couldWrite;
        lock (Queue)
            couldWrite = Writer.TryWrite(entry);
        if (!couldWrite)
            entry.ResultSource.TrySetException(ChannelClosedExceptionInstance);
        return entry.ResultSource.Task;
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var flow = await Host.Flows.GetOrStart(FlowId, cancellationToken).ConfigureAwait(false);
        flow = flow.Clone();
        flow.Initialize(flow.Id, flow.Version, flow.Step, this);
        var options = flow.GetOptions();

        var clock = Timeouts.Generic.Clock;
        var reader = Queue.Reader;

        var gracefulStopCts = cancellationToken.CreateDelayedTokenSource(options.GracefulDisposeDelay);
        var gracefulStopToken = gracefulStopCts.Token;
        try {
            while (true) {
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
                    Timeouts.Generic.AddOrUpdateToLater(this, clock.Now + options.KeepAliveFor);
                    if (!await canReadTask.ConfigureAwait(false))
                        return;

                    // Un-enlist this worklet for timeout-based removal
                    Timeouts.Generic.Remove(this);
                }
                if (!reader.TryRead(out var entry))
                    continue;

                if (entry.CancellationToken.IsCancellationRequested) {
                    // Entry is cancelled by the moment we dequeue it
                    entry.ResultSource.TrySetCanceled(entry.CancellationToken);
                    continue;
                }
                if (cancellationToken.IsCancellationRequested) {
                    // The worklet is already terminating
                    entry.ResultSource.TrySetException(new ChannelClosedException());
                    continue;
                }

                var backup = flow.Clone();
                try {
                    var evt = entry.Event;
                    while (true) {
                        var transition = await flow.HandleEvent(evt, gracefulStopToken).ConfigureAwait(false);
                        if (transition.Step == FlowSteps.MustRemove) {
                            Log.LogInformation("'{Id}' is removed", flow.Id);
                            entry.ResultSource.TrySetResult(0);
                            return;
                        }
                        if (transition.MustWait)
                            break;
                        evt = null;
                    }
                    entry.ResultSource.TrySetResult(flow.Version);
                }
                catch (Exception e) {
                    flow = backup;
                    if (e.IsCancellationOf(gracefulStopToken)) {
                        entry.ResultSource.TrySetCanceled(gracefulStopToken);
                        throw;
                    }

                    entry.ResultSource.TrySetException(e);
                    Log.LogError("'{Id}' @ {NextStep} failed", flow.Id, flow.Step);
                }
            }
        }
        finally {
            gracefulStopCts.CancelAndDisposeSilently();
            try {
                // Cancel remaining entries
                await foreach (var entry in reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
                    entry.ResultSource.TrySetException(ChannelClosedExceptionInstance);
            }
            catch {
                // Intended
            }
            Shard.Worklets.TryRemove(FlowId, this);
        }
    }

    void IGenericTimeoutHandler.OnTimeout()
    {
        lock (Queue) {
            // If there are any queued items, we aren't shutting ourselves down
            if (Queue.Reader.TryPeek(out _))
                return;

            // OnRun implements a graceful stop, so any in-progress items will still be processed
            _ = DisposeAsync();
        }
    }

    // Nested types

    public readonly record struct QueueEntry(
        object? Event,
        CancellationToken CancellationToken
    ) {
        public TaskCompletionSource<long> ResultSource { get; init; } = new();
    }
}
