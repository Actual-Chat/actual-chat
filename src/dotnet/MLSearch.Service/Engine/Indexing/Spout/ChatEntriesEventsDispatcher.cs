using ActualChat.Chat.Events;
using ActualChat.Commands;

namespace ActualChat.MLSearch.Engine.Indexing.Spout;

public interface IChatEntriesEventsDispatcher
{ }

public class ChatEntriesEventsDispatcher (ICommander commander) : IComputeService, IChatEntriesEventsDispatcher
{
    [EventHandler]
    // ReSharper disable once UnusedMember.Global
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var e = new MLSearch_TriggerContinueChatIndexing(eventCommand.Entry.ChatId);
        await commander.Call(e, cancellationToken).ConfigureAwait(false);
    }
}
