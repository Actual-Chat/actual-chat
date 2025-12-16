using ActualLab.CommandR.Operations;

namespace ActualChat.Flows.Infrastructure;

public class FlowRuntime(Flow flow, FlowHub hub, CancellationToken cancellationToken)
    : IHasServices, IServiceProvider
{
    public Flow Flow { get; } = flow;
    public FlowHub Hub { get; } = hub;
    public IServiceProvider Services => Hub.Services;
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public ILogger Log => field ??= Services.LogFor(Flow.GetType());

    // Properties
    public bool AutoCommit { get; set; } = true;
    public TimeSpan DefaultResumeDelayQuanta { get; set; }

    // Events
    public List<object?> StagedEvents { get; } = new();

    // IServiceProvider

    public object? GetService(Type serviceType)
        => Services.GetService(serviceType);

    // StageResume

    public FlowResumeEvent StageResume()
        => StageResumeAt(Flow.Id, Hub.SystemNow);
    public FlowResumeEvent StageResumeIn(TimeSpan delayBy)
        => StageResumeAt(Flow.Id, Hub.SystemNow + delayBy);
    public FlowResumeEvent StageResumeAt(Moment delayUntil)
        => StageResumeAt(Flow.Id, delayUntil);

    public FlowResumeEvent StageResume(FlowId flowId)
        => StageResumeAt(flowId, Hub.SystemNow);
    public FlowResumeEvent StageResumeIn(FlowId flowId, TimeSpan delayBy)
        => StageResumeAt(flowId, Hub.SystemNow + delayBy);
    public FlowResumeEvent StageResumeAt(FlowId flowId, Moment delayUntil)
    {
        var e = new FlowResumeEvent(flowId).WithDelay(delayUntil, DefaultResumeDelayQuanta);
        StagedEvents.Add(e);
        return e;
    }

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

    private OperationEvent[] GetStagedOperationEvents()
    {
        var buffer = ArrayBuffer<OperationEvent>.Lease(true, Math.Min(8, StagedEvents.Count));
        try {
            foreach (var e in StagedEvents) {
                if (e is null)
                    continue;
                if (e is IOperationEventSource operationEventSource)
                    buffer.Add(operationEventSource.ToOperationEvent());
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
