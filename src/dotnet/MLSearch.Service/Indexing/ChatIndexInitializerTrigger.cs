
using ActualChat.MLSearch.Indexing.Initializer;

namespace ActualChat.MLSearch.Indexing;

internal class ChatIndexInitializerTrigger(IChatIndexInitializer indexInitializer)
    : IChatIndexInitializerTrigger, IComputeService
{
    public virtual async Task OnCommand(MLSearch_TriggerChatIndexingCompletion e, CancellationToken cancellationToken)
        => await indexInitializer.PostAsync(e, cancellationToken).ConfigureAwait(false);
}
