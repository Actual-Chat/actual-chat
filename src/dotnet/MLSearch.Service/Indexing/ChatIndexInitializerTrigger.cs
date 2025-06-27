
using ActualChat.MLSearch.Indexing.Initializer;

namespace ActualChat.MLSearch.Indexing;

internal class ChatIndexInitializerTrigger(IChatIndexInitializer indexInitializer)
    : IChatIndexInitializerTrigger
{
    // [CommandHandler]
    public virtual async Task OnCommand(MLSearch_TriggerChatIndexingCompletion e, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        await indexInitializer.PostAsync(e, cancellationToken).ConfigureAwait(false);
    }
}
