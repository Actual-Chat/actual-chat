
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
}
