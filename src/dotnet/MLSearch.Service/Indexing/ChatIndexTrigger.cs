using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;

namespace ActualChat.MLSearch.Indexing;

// NOTE: solution drawback
// There is a chance that the receiving counter part will fail to complete
// event handling while the event will be marked as complete.
// This means: At most once logic.

internal class ChatIndexTrigger(ICommander commander, IChatIndexFilter chatIndexFilter, IWorkerPool<MLSearch_TriggerChatIndexing, ChatId, ChatId> workerPool)
    : IChatIndexTrigger, IComputeService
{
    // ReSharper disable once UnusedMember.Global
    // [CommandHandler]
    public virtual async Task OnCommand(MLSearch_TriggerChatIndexing e, CancellationToken cancellationToken)
        => await workerPool.PostAsync(e, cancellationToken).ConfigureAwait(false);

    // ReSharper disable once UnusedMember.Global
    // [CommandHandler]
    public virtual async Task OnCancelCommand(MLSearch_CancelChatIndexing e, CancellationToken cancellationToken)
        => await workerPool.CancelAsync(e, cancellationToken).ConfigureAwait(false);

    // ReSharper disable once UnusedMember.Global
    // [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        bool shouldBeIndexed = await chatIndexFilter.ChatShouldBeIndexed(eventCommand.Entry.ChatId, cancellationToken).ConfigureAwait(false);
        if (!shouldBeIndexed)
            return;
        // A normal conversation.
        var e = new MLSearch_TriggerChatIndexing(eventCommand.Entry.ChatId);
        await commander.Call(e, cancellationToken).ConfigureAwait(false);
    }
}
