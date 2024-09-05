using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.Queues;

namespace ActualChat.MLSearch.Indexing;

// NOTE: solution drawback
// There is a chance that the receiving counter part will fail to complete
// event handling while the event will be marked as complete.
// This means: At most once logic.

internal class ChatIndexTrigger(
    IQueues queues,
    IWorkerPool<MLSearch_TriggerChatIndexing,
    (ChatId, IndexingKind), ChatId> workerPool
    ) : IChatIndexTrigger
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
        var indexContentCmd = new MLSearch_TriggerChatIndexing(eventCommand.Entry.ChatId, IndexingKind.ChatContent);
        await queues.Enqueue(indexContentCmd, cancellationToken).ConfigureAwait(false);

        var indexEntriesCmd = new MLSearch_TriggerChatIndexing(eventCommand.Entry.ChatId, IndexingKind.ChatEntries);
        await queues.Enqueue(indexEntriesCmd, cancellationToken).ConfigureAwait(false);
    }

    // ReSharper disable once UnusedMember.Global
    // [EventHandler]
    public virtual async Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var e = new MLSearch_TriggerChatIndexing(eventCommand.Chat.Id, IndexingKind.ChatInfo);
        await queues.Enqueue(e, cancellationToken).ConfigureAwait(false);
    }
}
