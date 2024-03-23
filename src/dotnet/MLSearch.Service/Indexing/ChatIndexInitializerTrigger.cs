
namespace ActualChat.MLSearch.Indexing;

internal class ChatIndexInitializerTrigger(IChatIndexInitializer indexInitializer)
    : IChatIndexInitializerTrigger, IComputeService
{
    public async Task OnCommand(MLSearch_TriggerChatIndexingCompletion e, CancellationToken cancellationToken)
        => await indexInitializer.PostAsync(e, cancellationToken).ConfigureAwait(false);
}
