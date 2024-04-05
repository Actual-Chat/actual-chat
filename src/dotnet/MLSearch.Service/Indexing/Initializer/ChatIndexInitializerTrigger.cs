
namespace ActualChat.MLSearch.Indexing.Initializer;

internal class ChatIndexInitializerTrigger(IChatIndexInitializer indexInitializer)
    : IChatIndexInitializerTrigger, IComputeService
{
    public virtual async Task OnCommand(MLSearch_TriggerChatIndexingCompletion e, CancellationToken cancellationToken)
        => await indexInitializer.PostAsync(e, cancellationToken).ConfigureAwait(false);
}
