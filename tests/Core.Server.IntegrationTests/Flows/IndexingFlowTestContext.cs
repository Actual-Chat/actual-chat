using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public sealed class IndexingFlowTestContext
{
    private readonly Dictionary<Symbol, Queue<BatchIndexingResult<long>>> _batches = new();
    private readonly Dictionary<Symbol, List<FlowTransition>> _appliedTransitions = new();

    public void Add(Symbol id, params BatchIndexingResult<long>[] results)
    {
        var queue = _batches.GetOrAdd(id);
        foreach (var result in results)
            queue.Enqueue(result);
    }

    public BatchIndexingResult<long> Next(Symbol id)
        => _batches[id].Dequeue();

    public IReadOnlyCollection<BatchIndexingResult<long>> Remaining(Symbol id)
        => _batches[id];

    public void OnTransition(Symbol id, FlowTransition transition)
        => _appliedTransitions.GetOrAdd(id).Add(transition);

    public IReadOnlyList<FlowTransition> ListTransitions(Symbol id)
        => _appliedTransitions.GetValueOrDefault(id, []);
}
