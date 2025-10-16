using ActualLab.CommandR.Operations;
using ActualLab.Diagnostics;

namespace ActualChat.Flows.Infrastructure;

public sealed class FlowRuntime(Flow flow, IServiceProvider services, CancellationToken cancellationToken)
{
    public Flow Flow { get; } = flow;
    public IServiceProvider Services { get; } = services;
    public CancellationToken CancellationToken { get; } = cancellationToken;

    // Most useful service shortcuts
    [field: AllowNull, MaybeNull]
    public ICommander Commander => field ??= Services.Commander();
    [field: AllowNull, MaybeNull]
    public MomentClockSet Clocks => field ??= Services.Clocks();
    public Moment Now => Clocks.SystemClock.Now;

    // Logging
    [field: AllowNull, MaybeNull]
    public ILogger Log => field ??= Services.LogFor(Flow.GetType());
    public ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Flows);

    // NewResumeEvent

    public FlowResumeEvent NewResumeEvent(TimeSpan delayBy = default, TimeSpan delayQuanta = default)
        => NewResumeEvent(Now + delayBy, delayQuanta);
    public FlowResumeEvent NewResumeEvent(Moment delayUntil = default, TimeSpan delayQuanta = default)
        => new (Flow.Id) {
            DelayUntil = delayUntil,
            DelayQuanta = delayQuanta,
        };

    public FlowResumeEvent NewResumeEvent(FlowId flowId, TimeSpan delayBy = default, TimeSpan delayQuanta = default)
        => NewResumeEvent(flowId, Now + delayBy, delayQuanta);
    public FlowResumeEvent NewResumeEvent(FlowId flowId, Moment delayUntil = default, TimeSpan delayQuanta = default)
        => new (flowId) {
            DelayUntil = delayUntil,
            DelayQuanta = delayQuanta,
        };

    // Store

    public Task Store()
        => Store(Array.Empty<OperationEvent>());

    public Task Store(params ReadOnlySpan<IFlowEvent> events)
    {
        OperationEvent[] operationEvents;
        var now = Now;
        var buffer = ArrayBuffer<OperationEvent>.Lease(true);
        try {
            foreach (var @event in events)
                buffer.Add(@event.ToOperationEvent(now));
            operationEvents = buffer.ToArray();
        }
        finally {
            buffer.Release();
        }
        return Store(operationEvents);
    }

    public Task Store(params IEnumerable<IFlowEvent> events)
    {
        OperationEvent[] operationEvents;
        var now = Now;
        var buffer = ArrayBuffer<OperationEvent>.Lease(true);
        try {
            foreach (var @event in events)
                buffer.Add(@event.ToOperationEvent(now));
            operationEvents = buffer.ToArray();
        }
        finally {
            buffer.Release();
        }
        return Store(operationEvents);
    }

    public async Task Store(OperationEvent[] events)
    {
        // Always runs locally
        var storeCommand = new Flows_Store(Flow.Id, Flow.Version) {
            Events = events.Length == 0 ? null : events.ToArray(),
        };

        var version = await Commander.Call(storeCommand, true, CancellationToken).ConfigureAwait(false);
        ((IFlowImpl)Flow).Initialize(Flow.Id, version);
    }
}
