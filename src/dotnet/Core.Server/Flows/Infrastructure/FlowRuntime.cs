using ActualLab.CommandR.Operations;
using ActualLab.Diagnostics;

namespace ActualChat.Flows.Infrastructure;

public class FlowRuntime(Flow flow, IServiceProvider services, CancellationToken cancellationToken)
    : IHasServices, IServiceProvider
{
    public Flow Flow { get; } = flow;
    public IServiceProvider Services { get; } = services;
    public CancellationToken CancellationToken { get; } = cancellationToken;

    // Services, service shortcuts
    public ICommander Commander => field ??= Services.Commander();
    public MomentClockSet Clocks => field ??= Services.Clocks();
    public Moment Now => Clocks.SystemClock.Now;
    public ILogger Log => field ??= Services.LogFor(Flow.GetType());
    public ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Flows);

    // Properties
    public bool AutoCommit { get; set; } = true;
    public TimeSpan DefaultResumeDelayQuanta { get; set; }

    // Events
    public List<object?> StagedEvents { get; } = new();

    // IServiceProvider

    public object? GetService(Type serviceType)
        => Services.GetService(serviceType);

    // ScheduleResume

    public FlowResume ScheduleResume()
        => ScheduleResumeAt(Flow.Id, Now);
    public FlowResume ScheduleResumeIn(TimeSpan delayBy)
        => ScheduleResumeAt(Flow.Id, Now + delayBy);
    public FlowResume ScheduleResumeAt(Moment delayUntil)
        => ScheduleResumeAt(Flow.Id, delayUntil);

    public FlowResume ScheduleResume(FlowId flowId)
        => ScheduleResumeAt(flowId, Now);
    public FlowResume ScheduleResumeIn(FlowId flowId, TimeSpan delayBy)
        => ScheduleResumeAt(flowId, Now + delayBy);
    public FlowResume ScheduleResumeAt(FlowId flowId, Moment delayUntil)
    {
        var e = new FlowResume(flowId, delayUntil) {
            DelayQuanta = DefaultResumeDelayQuanta,
        };
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
        var version = await Commander.Call(storeCommand, cancellationToken).ConfigureAwait(false);

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
        var now = Now;
        var buffer = ArrayBuffer<OperationEvent>.Lease(true, Math.Min(8, StagedEvents.Count));
        try {
            foreach (var e in StagedEvents) {
                if (e is null)
                    continue;
                if (e is IFlowEvent flowEvent)
                    buffer.Add(flowEvent.ToOperationEvent(now));
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
