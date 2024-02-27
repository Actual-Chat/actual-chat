using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.SearchEngine.OpenSearch.Extensions;
using OpenSearch.Client;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch.Stream;

internal interface ISharded
{
    public abstract static int ShardCount { get; }
}

internal interface IStreamTopology: ISharded
{
    Task Execute(int shardIndex, int shardCount, CancellationToken cancellationToken);
}

internal class ChatEntriesStreamTopology(
    StreamCursor<Moment> cursor,
    Sink<IndexedDocument, Id> sink,
    ILoggerSource? loggerSource
): IStreamTopology
{
    public static int ShardCount => 10;


    protected async virtual Task<(
        IEnumerable<IndexedDocument> created,
        IEnumerable<IndexedDocument> updated,
        IEnumerable<IndexedDocument> deleted
        )> Next(int shardIndex, int shardCount)
    {
        // Generates an update of changes. It's up to the this method
        // to decide how big is it going to be. Although if it's too
        // big a chance of a failure may be that high that it would
        // never progress since one failure would fail the entire set.
        // TODO:
        // - read a batch of updates
        // - get the last items Moment value
        // - load all updates with the items Moment same value
        List<IndexedDocument> deleted = [];
        List<IndexedDocument> updated = [];
        List<IndexedDocument> created = [];

        return (created, updated, deleted);
    }

    public async Task Execute(int shardIndex, int shardCount, CancellationToken cancellationToken)
    {
        var (created, deleted, updated) = await Next()
            .ConfigureAwait(false);

        IEnumerable<IndexedDocument> updates = created.Concat(updated);
        IEnumerable<Id> deletes = deleted.Select(e => e.Id());
        await sink.Execute(updates, deletes, cancellationToken)
            .ConfigureAwait(false);
    }
}
