using ActualChat.MLSearch.ApiAdapters.ShardWorker;

namespace ActualChat.MLSearch.Indexing;

internal interface IChatIndexerWorker: IWorker<MLSearch_TriggerChatIndexing>;

internal class ChatIndexerWorker(
    int maxIterationCount,
    IDataIndexer<ChatId> dataIndexer,
    ICommander commander
) : IChatIndexerWorker
{
    public async Task ExecuteAsync(MLSearch_TriggerChatIndexing job, CancellationToken cancellationToken)
    {
        bool continueIndexing = true;
        for (var iterationCount = 0; continueIndexing && iterationCount < maxIterationCount; iterationCount++) {
            var result = await dataIndexer.IndexNextAsync(job.Id, cancellationToken).ConfigureAwait(false);
            continueIndexing = !(result.IsEndReached || cancellationToken.IsCancellationRequested);
        }
        if (continueIndexing) {
            await commander.Call(job, cancellationToken).ConfigureAwait(false);
        }
        else if (!cancellationToken.IsCancellationRequested) {
            var completionNotification = new MLSearch_TriggerChatIndexingCompletion(job.Id);
            await commander.Call(completionNotification, cancellationToken).ConfigureAwait(false);
        }
    }
}
