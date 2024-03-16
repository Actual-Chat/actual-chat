
using ActualChat.MLSearch.ApiAdapters;

namespace ActualChat.MLSearch.Indexing;

internal interface IChatIndexerDispatcher
{
    ValueTask DispatchAsync(MLSearch_TriggerChatIndexing input, CancellationToken cancellationToken);
    Task ExecuteAsync(int shardIndex, CancellationToken cancellationToken);
}

internal class ChatIndexerDispatcher(
    IShardIndexResolver<ChatId> shardIndexResolver,
    IChatIndexerWorkerFactory workerFactory,
    ShardScheme shardScheme
): IChatIndexerDispatcher
{
    private readonly ConcurrentDictionary<int, IChatIndexerWorker> _workers = new();

    public async ValueTask DispatchAsync(MLSearch_TriggerChatIndexing input, CancellationToken cancellationToken)
    {
        var shardIndex = shardIndexResolver.Resolve(input, shardScheme);
        if (!_workers.TryGetValue(shardIndex, out var worker)) {
            throw StandardError.NotFound<IChatIndexerWorker>(
                $"{nameof(IChatIndexerWorker)} instance for the shard #{shardIndex.Format()} is not found.");
        }
        await worker.PostAsync(input, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteAsync(int shardIndex, CancellationToken cancellationToken)
    {
        var worker = _workers.AddOrUpdate(shardIndex,
            (key, factory) => factory.Create(key),
            (key, _, factory) => factory.Create(key),
            workerFactory);
        try {
            await worker.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            // Clean up dictionary of workers if it still contains worker being stopped
            _workers.TryRemove(new KeyValuePair<int, IChatIndexerWorker>(shardIndex, worker));
        }
    }
}
