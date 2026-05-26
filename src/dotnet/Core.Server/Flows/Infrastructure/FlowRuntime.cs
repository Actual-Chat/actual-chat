using ActualLab.CommandR.Operations;

namespace ActualChat.Flows.Infrastructure;

public class FlowRuntime(Flow flow, FlowHub hub, CancellationToken cancellationToken) : IHasServices
{
    public Flow Flow { get; } = flow;
    public FlowHub Hub { get; } = hub;
    public IServiceProvider Services => Hub.Services;
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public FlowDef FlowDef => field ??= Hub.Defs.ByType[Flow.GetType()];
    public ILogger Log => field ??= Services.LogFor(Flow.GetType());

    // Properties
    public bool AutoCommit { get; set; } = true;
    // Events
    public List<object?> StagedEvents { get; } = new();

    // StageResume

    public FlowResumeEvent StageResume()
    {
        // Immediate self-resume (e.g. chained Quota-based continuation in IndexingFlow):
        // must NOT inherit the flow's DelayQuanta — that bucket-Uuid dedup is meant to
        // coalesce external triggers, but it silently drops consecutive self-resumes
        // that fall into the same bucket, breaking Quota-driven Run chains.
        var e = new FlowResumeEvent(Flow.Id, Hub).WithDelayQuanta(TimeSpan.Zero);
        StagedEvents.Add(e);
        return e;
    }

    public FlowResumeEvent StageResumeIn(TimeSpan delayBy)
        => StageResumeAt(Flow.Id, Hub.SystemNow + delayBy);
    public FlowResumeEvent StageResumeIn(TimeSpan delayBy, TimeSpan? delayQuanta)
        => StageResumeAt(Flow.Id, Hub.SystemNow + delayBy, delayQuanta);

    public FlowResumeEvent StageResumeAt(Moment delayUntil)
        => StageResumeAt(Flow.Id, delayUntil);
    public FlowResumeEvent StageResumeAt(Moment delayUntil, TimeSpan? delayQuanta)
        => StageResumeAt(Flow.Id, delayUntil, delayQuanta);

    // Commit

    public async Task Commit(CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.CanBeCanceled)
            cancellationToken = CancellationToken;

        // Always runs locally
        var events = GetStagedOperationEvents();
        var storeCommand = new Flows_Store(Flow.Id, Flow.Version) {
            Flow = Flow,
            Events = events,
        };
        var version = await Hub.Commander.Call(storeCommand, cancellationToken).ConfigureAwait(false);

        // Update own state
        StagedEvents.Clear();

        // Update Flow state
        ((IFlowImpl)Flow).Version = version;
        var sb = Flow.Console.Suffix;
        var console = sb.Length != 0 && sb[^1] == FlowConsole.NewLine
            ? sb.ToString(0, sb.Length - 1) // Remove the last new line
            : sb.ToString();
        Flow.Console.Commit().LogSection("[Commit]");

        if (console.IsNullOrEmpty())
            Log.LogInformation("`{FlowId}` committed", Flow.Id.Value);
        else
            Log.LogInformation("`{FlowId}` committed, console (new lines only):\n{Console}", Flow.Id.Value, console);
    }

    // Private methods

    private FlowResumeEvent StageResumeAt(FlowId flowId, Moment delayUntil)
    {
        var e = new FlowResumeEvent(flowId, Hub).WithDelay(delayUntil);
        // Log.LogInformation("Staged event: {Event}", e);
        StagedEvents.Add(e);
        return e;
    }

    private FlowResumeEvent StageResumeAt(FlowId flowId, Moment delayUntil, TimeSpan? delayQuanta)
    {
        var e = new FlowResumeEvent(flowId, Hub).WithDelay(delayUntil, delayQuanta);
        // Log.LogInformation("Staged event: {Event}", e);
        StagedEvents.Add(e);
        return e;
    }

    private OperationEvent[] GetStagedOperationEvents()
    {
        var buffer = ArrayBuffer<OperationEvent>.Lease(true, Math.Min(8, StagedEvents.Count));
        try {
            foreach (var e in StagedEvents) {
                if (e is null)
                    continue;
                if (e is IOperationEventSource operationEventSource)
                    buffer.Add(operationEventSource.ToOperationEvent(Services));
                else if (e is OperationEvent operationEvent)
                    buffer.Add(operationEvent);
                else
                    Log.LogError("Unknown event type in StagedEvents: {Type}", e.GetType().GetName());
            }
            return buffer.ToArray();
        }
        finally {
            buffer.Release();
        }
    }
}
