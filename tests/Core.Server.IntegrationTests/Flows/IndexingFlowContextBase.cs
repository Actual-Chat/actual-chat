using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public abstract class IndexingFlowContextBase<TBatch>(MomentClockSet clocks)
{
    protected readonly Dictionary<Symbol, Queue<TBatch>> Batches = new();
    private readonly Dictionary<Symbol, List<TBatch>> _processedBatches = new();
    private readonly Dictionary<Symbol, List<FlowTransition>> _appliedTransitions = new();
    private readonly Dictionary<Symbol, Queue<TailHandler>> _tailHandlers = new();
    private readonly Dictionary<Symbol, int?> _currentFlowSetVersionOverrides = new();

    public void Add(Symbol id, params IEnumerable<TBatch> batches)
    {
        var queue = Batches.GetOrAdd(id);
        foreach (var result in batches)
            queue.Enqueue(result);
    }

    public abstract TBatch Next(Symbol id);

    public Queue<TBatch> ListRemaining(Symbol id)
        => Batches[id];

    public void OnTransition(Symbol id, FlowTransition transition)
        => _appliedTransitions.GetOrAdd(id).Add(transition);

    public List<(string Step, TimeSpan? HardResumeIn)> ListTransitions(Symbol id, Moment? now = null)
        => _appliedTransitions.GetValueOrDefault(id, [])
            .Select(transition => (transition.Step.Value,
                transition.HardResumeAt == Flow.InfiniteHardResumeAt
                    ? TimeSpan.MaxValue
                    : transition.HardResumeAt - (now ?? clocks.SystemClock.Now)))
            .ToList();

    public void OnProcessed(Symbol id, TBatch batch)
        => _processedBatches.GetOrAdd(id).Add(batch);

    public List<TBatch> ListProcessed(Symbol id, bool skipEmpty = true)
    {
        var list = _processedBatches.GetValueOrDefault(id, []);
        return !skipEmpty ? list : list.Where(HasProcessedAnyItems).ToList();
    }

    public void AddTailHandler(Symbol id, TailHandler handler)
        => _tailHandlers.GetOrAdd(id).Enqueue(handler);

    public Task<IndexingFlowTransitionKind?> HandleTail(Symbol id, bool hasProcessedAnyItems)
        => _tailHandlers.GetValueOrDefault(id)?.TryDequeue(out TailHandler? handler) == true
            ? handler(hasProcessedAnyItems)
            : Task.FromResult<IndexingFlowTransitionKind?>(null);

    protected abstract bool HasProcessedAnyItems(TBatch batch);

    public int? GetCurrentFlowSetVersionOverride(Symbol id)
        => _currentFlowSetVersionOverrides.GetValueOrDefault(id);

    public int? SetCurrentFlowSetVersionOverride(Symbol id, int? value)
        => _currentFlowSetVersionOverrides[id] = value;

    public delegate Task<IndexingFlowTransitionKind?> TailHandler(bool hasProcessedAnyItems);
}
