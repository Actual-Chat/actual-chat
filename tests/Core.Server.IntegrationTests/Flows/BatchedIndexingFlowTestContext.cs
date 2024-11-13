using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public sealed class BatchedIndexingFlowTestContext<TItem>
{
    private readonly Dictionary<Symbol, Queue<IReadOnlyList<TItem>>> _batches = new();
    private readonly Dictionary<Symbol, List<IReadOnlyList<TItem>>> _processedBatches = new();
    private readonly Dictionary<Symbol, List<FlowTransition>> _appliedTransitions = new();
    private readonly Dictionary<Symbol, Queue<Func<Task<bool>>>> _tailHandlers = new();

    public void Add(Symbol id, params IReadOnlyList<TItem>[] batches)
    {
        var queue = _batches.GetOrAdd(id);
        foreach (var result in batches)
            queue.Enqueue(result);
    }

    public IReadOnlyList<TItem> Next(Symbol id)
        => _batches[id].Dequeue();

    public Queue<IReadOnlyList<TItem>> ListRemaining(Symbol id)
        => _batches[id];

    public void OnTransition(Symbol id, FlowTransition transition)
        => _appliedTransitions.GetOrAdd(id).Add(transition);

    public IReadOnlyList<FlowTransition> ListTransitions(Symbol id)
        => _appliedTransitions.GetValueOrDefault(id, []);

    public void OnProcessed(Symbol id, IReadOnlyList<TItem> batch)
        => _processedBatches.GetOrAdd(id).Add(batch);

    public List<IReadOnlyList<TItem>> ListProcessed(Symbol id)
        => _processedBatches.GetValueOrDefault(id, []);

    public void AddTailHandler(Symbol id, Func<Task<bool>> handler)
        => _tailHandlers.GetOrAdd(id).Enqueue(handler);

    public Task<bool> OnTailReached(Symbol id)
        => _tailHandlers.GetValueOrDefault(id)?.TryDequeue(out Func<Task<bool>>? handler) == true
            ? handler()
            : ActualLab.Async.TaskExt.TrueTask;
}
