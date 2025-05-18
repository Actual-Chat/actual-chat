
using ActualChat.MLSearch.Indexing.Initializer;

namespace ActualChat.MLSearch.Indexing;

internal class ChatIndexInitializerTrigger(IChatIndexInitializer indexInitializer)
    : IChatIndexInitializerTrigger
{
    public virtual async Task OnContinuation(MLSearch_SignalChatIndexingContinuation e, CancellationToken cancellationToken)
        => await indexInitializer.PostAsync(e, cancellationToken).ConfigureAwait(false);

    public virtual async Task OnCompletion(MLSearch_SignalChatIndexingCompletion e, CancellationToken cancellationToken)
        => await indexInitializer.PostAsync(e, cancellationToken).ConfigureAwait(false);
}
