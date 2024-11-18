using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public abstract class IndexingFlowContextBase<TBatch>(MomentClockSet clocks)
{
private readonly Dictionary<Symbol, Queue<TBatch>> _batches = new();
    private readonly Dictionary<Symbol, List<TBatch>> _processedBatches = new();
    private readonly Dictionary<Symbol, List<FlowTransition>> _appliedTransitions = new();
    private readonly Dictionary<Symbol, Queue<TailHandler>> _tailHandlers = new();

    public void Add(Symbol id, params IEnumerable<TBatch> batches)
    {
        var queue = _batches.GetOrAdd(id);
        foreach (var result in batches)
            queue.Enqueue(result);
    }

    public TBatch Next(Symbol id)
        => _batches[id].TryDequeue(out var batch) ? batch : Default();

    public Queue<TBatch> ListRemaining(Symbol id)
        => _batches[id];

    public void OnTransition(Symbol id, FlowTransition transition)
        => _appliedTransitions.GetOrAdd(id).Add(transition);

    public List<(string Step, TimeSpan? HardResumeIn)> ListTransitions(Symbol id)
        => _appliedTransitions.GetValueOrDefault(id, [])
            .Select(transition => (transition.Step.Value,
                transition.HardResumeAt == Flow.InfiniteHardResumeAt
                    ? TimeSpan.MaxValue
                    : transition.HardResumeAt - clocks.SystemClock.Now))
            .ToList();

    public void OnProcessed(Symbol id, TBatch batch)
        => _processedBatches.GetOrAdd(id).Add(batch);

    public List<TBatch> ListProcessed(Symbol id, bool skipEmpty = true)
    {
        var list = _processedBatches.GetValueOrDefault(id, []);
        return !skipEmpty ? list : list.Where(x => GetCount(x) > 0).ToList();
    }

    public void AddTailHandler(Symbol id, TailHandler handler)
        => _tailHandlers.GetOrAdd(id).Enqueue(handler);

    public Task<bool> OnTailReached(Symbol id, int processCount)
        => _tailHandlers.GetValueOrDefault(id)?.TryDequeue(out TailHandler? handler) == true
            ? handler(processCount)
            : ActualLab.Async.TaskExt.TrueTask;

    protected virtual TBatch Default()
        => throw new NotImplementedException();
    protected abstract int GetCount(TBatch batch);

    public delegate Task<bool> TailHandler(int processedCount);
}
