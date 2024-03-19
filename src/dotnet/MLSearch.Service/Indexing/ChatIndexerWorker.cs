using ActualChat.MLSearch.ApiAdapters.ShardWorker;

namespace ActualChat.MLSearch.Indexing;

internal interface IChatIndexerWorker: IWorker<MLSearch_TriggerChatIndexing>;

internal class ChatIndexerWorker(
    IDataIndexer<ChatId> dataIndexer
) : IChatIndexerWorker
{
    public async Task ExecuteAsync(MLSearch_TriggerChatIndexing input, CancellationToken cancellationToken)
    {
        bool continueIndexing;
        do {
            var result = await dataIndexer.IndexNextAsync(input.Id, cancellationToken).ConfigureAwait(false);
            continueIndexing = !(result.IsEndReached || cancellationToken.IsCancellationRequested);
        }
        while (continueIndexing);
    }
}
