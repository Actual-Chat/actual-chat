using ActualChat.Chat.Events;
using ActualChat.Commands;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch.Indexing.Spout;

public class ChatEntriesEventsDispatcher (ICommander commander) : IComputeService
{
    [EventHandler]
    // ReSharper disable once UnusedMember.Global
    public async Task ProcessTextEntryChangedEventEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var e = new MLSearch_TriggerContinueChatIndexing(eventCommand.Entry.ChatId);
        await commander.Call(e, cancellationToken).ConfigureAwait(false);
    }
}
