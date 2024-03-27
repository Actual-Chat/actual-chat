using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.MLSearch.Indexing;

namespace ActualChat.MLSearch.Bot;

internal interface IChatBotWorker: IWorker<MLSearch_TriggerContinueConversationWithBot>;

internal class ChatBotWorker(
    IDataIndexer<ChatId> dataIndexer
) : IChatBotWorker
{
    public async Task ExecuteAsync(MLSearch_TriggerContinueConversationWithBot input, CancellationToken cancellationToken)
    {
        bool continueIndexing;
        do {
            var result = await dataIndexer.IndexNextAsync(input.Id, cancellationToken).ConfigureAwait(false);
            continueIndexing = !(result.IsEndReached || cancellationToken.IsCancellationRequested);
        }
        while (continueIndexing);
    }
}
