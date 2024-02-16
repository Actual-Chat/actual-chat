using ActualChat.Chat.Events;
using ActualChat.Commands;

namespace ActualChat.MLSearch;

internal class MLSearchBackend: IMLSearchBackend
{
    // Commands
    public virtual async Task OnStartSearch(MLSearchBackend_Start command, CancellationToken cancellationToken)
    {
        // Extract chat history
        // Try to build search query out of it
        // if (success)
        //    run vector search (directly of via command ? )
        //    if there is previously started search for this chat, cancel it
        // else
        //    post error message or ask some question

        // On vector search completion
        // build answer using chat history and vector search response as an input
        // post response
    }

    public virtual async Task OnUpsertIndex(MLSearchBackend_UpsertIndex command, CancellationToken cancellationToken)
    {
        // Marks chat as updated
        // Schedules indexing
        // If indexing job is already waiding in a queue, does nothing
    }

    // Events

    [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        // if (isSearchChat) {
            // run MLSearchBackend_Start command
        //}

        // In parallel we are going to run MLSearchBackend_UpsertIndex command on every incoming update
        // The question is should we index search chats as well? (Probably yes)
    }
}
