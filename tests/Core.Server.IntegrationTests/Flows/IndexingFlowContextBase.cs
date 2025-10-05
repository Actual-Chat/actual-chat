using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public record TransitionInfo(/* Moment TransitionAt,*/ string Step, Moment? HardResumeAt, TimeSpan? HardResumeIn);

public abstract class IndexingFlowContextBase<TBatch>(MomentClockSet clocks)
{
    protected readonly Dictionary<string, Queue<TBatch>> Batches = new();
    private readonly Dictionary<string, List<TBatch>> _processedBatches = new();
    private readonly Dictionary<string, List<(Moment, LegacyFlowTransition)>> _appliedTransitions = new();
    private readonly Dictionary<string, Queue<TailHandler>> _tailHandlers = new();
    private readonly Dictionary<string, int?> _currentFlowSetVersionOverrides = new();

    private Moment Now => clocks.SystemClock.Now;

    public void Add(string id, params IEnumerable<TBatch> batches)
    {
        var queue = Batches.GetOrAdd(id);
        foreach (var result in batches)
            queue.Enqueue(result);
    }

    public abstract TBatch Next(string id);

    public Queue<TBatch> ListRemaining(string id)
        => Batches[id];

    public void OnTransition(string id, LegacyFlowTransition transition)
        => _appliedTransitions.GetOrAdd(id).Add((Now, transition));

    public List<TransitionInfo> ListTransitions(string id)
        => _appliedTransitions.GetValueOrDefault(id, [])
            .Select(tuple => {
                var (transitionAt, transition) = tuple;
                return new TransitionInfo(
                    /* transitionAt, */
                    transition.Step.Value,
                    transition.HardResumeAt,
                    transition.HardResumeAt == LegacyFlow.InfiniteHardResumeAt
                        ? TimeSpan.MaxValue
                        : transition.HardResumeAt - transitionAt);
            })
            .ToList();

    public void OnProcessed(string id, TBatch batch)
        => _processedBatches.GetOrAdd(id).Add(batch);

    public List<TBatch> ListProcessed(string id, bool skipEmpty = true)
    {
        var list = _processedBatches.GetValueOrDefault(id, []);
        return !skipEmpty ? list : list.Where(HasProcessedAnyItems).ToList();
    }

    public void AddTailHandler(string id, TailHandler handler)
        => _tailHandlers.GetOrAdd(id).Enqueue(handler);

    public Task<IndexingFlowTransitionKind?> HandleTail(string id, bool hasProcessedAnyItems)
        => _tailHandlers.GetValueOrDefault(id)?.TryDequeue(out TailHandler? handler) == true
            ? handler(hasProcessedAnyItems)
            : Task.FromResult<IndexingFlowTransitionKind?>(null);

    protected abstract bool HasProcessedAnyItems(TBatch batch);

    public int? GetCurrentFlowSetVersionOverride(string id)
        => _currentFlowSetVersionOverrides.GetValueOrDefault(id);

    public int? SetCurrentFlowSetVersionOverride(string id, int? value)
        => _currentFlowSetVersionOverrides[id] = value;

    public delegate Task<IndexingFlowTransitionKind?> TailHandler(bool hasProcessedAnyItems);
}
