using ActualLab.CommandR.Operations;
using ActualLab.Diagnostics;

namespace ActualChat.Flows.Infrastructure;

public sealed class FlowRuntime(Flow flow, IServiceProvider services, CancellationToken cancellationToken)
    : IHasServices, IServiceProvider
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

    public FlowResumeEvent NewResumeEvent(TimeSpan delayBy = default, TimeSpan delayQuanta = default, bool mustReset = false)
        => NewResumeEvent(Now + delayBy, delayQuanta, mustReset);
    public FlowResumeEvent NewResumeEvent(Moment delayUntil = default, TimeSpan delayQuanta = default, bool mustReset = false)
        => new (Flow.Id) {
            DelayUntil = delayUntil,
            DelayQuanta = delayQuanta,
            MustReset = mustReset,
        };

    public FlowResumeEvent NewResumeEvent(FlowId flowId, TimeSpan delayBy = default, TimeSpan delayQuanta = default, bool mustReset = false)
        => NewResumeEvent(flowId, Now + delayBy, delayQuanta, mustReset);
    public FlowResumeEvent NewResumeEvent(FlowId flowId, Moment delayUntil = default, TimeSpan delayQuanta = default, bool mustReset = false)
        => new (flowId) {
            DelayUntil = delayUntil,
            DelayQuanta = delayQuanta,
            MustReset = mustReset,
        };

    // Store

    public Task Store(CancellationToken cancellationToken = default)
        => Store(Array.Empty<OperationEvent>(), cancellationToken);
    public Task Store(IFlowEvent? event1, CancellationToken cancellationToken = default)
        => Store([event1], cancellationToken);
    public Task Store(IFlowEvent? event1, IFlowEvent? event2, CancellationToken cancellationToken = default)
        => Store([event1, event2], cancellationToken);
    public Task Store(IFlowEvent? event1, IFlowEvent? event2, IFlowEvent? event3, CancellationToken cancellationToken = default)
        => Store([event1, event2, event3], cancellationToken);

    public Task Store(ReadOnlySpan<IFlowEvent?> events, CancellationToken cancellationToken = default)
    {
        OperationEvent[] operationEvents;
        var now = Now;
        var buffer = ArrayBuffer<OperationEvent>.Lease(true);
        try {
            foreach (var e in events)
                if (e is not null)
                    buffer.Add(e.ToOperationEvent(now));
            operationEvents = buffer.ToArray();
        }
        finally {
            buffer.Release();
        }
        return Store(operationEvents, cancellationToken);
    }

    public Task Store(IEnumerable<IFlowEvent?> events, CancellationToken cancellationToken = default)
    {
        OperationEvent[] operationEvents;
        var now = Now;
        var buffer = ArrayBuffer<OperationEvent>.Lease(true);
        try {
            foreach (var e in events)
                if (e is not null)
                    buffer.Add(e.ToOperationEvent(now));
            operationEvents = buffer.ToArray();
        }
        finally {
            buffer.Release();
        }
        return Store(operationEvents, cancellationToken);
    }

    public async Task Store(OperationEvent[] events, CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.CanBeCanceled)
            cancellationToken = CancellationToken;

        // Always runs locally
        var storeCommand = new Flows_Store(Flow.Id, Flow.Version) {
            Flow = Flow,
            Events = events.Length == 0 ? null : events.ToArray(),
        };
        var version = await Commander.Call(storeCommand, cancellationToken).ConfigureAwait(false);
        ((IFlowImpl)Flow).Version = version;
    }

    // IServiceProvider

    public object? GetService(Type serviceType)
        => Services.GetService(serviceType);
}
