namespace ActualChat.Core.Server.IntegrationTests.Flows;

public abstract class IndexingFlowContextBase<TBatch>
    where TBatch : class
{
    protected readonly Dictionary<string, Queue<TBatch>> Batches = new();
    private readonly Dictionary<string, List<TBatch>> _processedBatches = new();

    public void Add(string id, params IEnumerable<TBatch> batches)
    {
        var queue = Batches.GetOrAdd(id);
        foreach (var result in batches)
            queue.Enqueue(result);
    }

    public abstract TBatch? Next(string id);

    public Queue<TBatch> ListRemaining(string id)
        => Batches[id];

    public void OnProcessed(string id, TBatch batch)
        => _processedBatches.GetOrAdd(id).Add(batch);

    public List<TBatch> ListProcessed(string id, bool skipEmpty = true)
    {
        var list = _processedBatches.GetValueOrDefault(id, []);
        return !skipEmpty ? list : list.Where(HasProcessedAnyItems).ToList();
    }

    protected abstract bool HasProcessedAnyItems(TBatch batch);
}
