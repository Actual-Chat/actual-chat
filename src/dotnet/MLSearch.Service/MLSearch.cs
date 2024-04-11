
namespace ActualChat.MLSearch;

internal class MLSearchImpl : IMLSearch
{
    public Task<MLSearchChat> OnCreate(MLSearch_CreateChat command, CancellationToken cancellationToken)
    {
        // This method is called from the client side
        // It creates a new ML search chat with two participants:
        //    The user who initiated the search and Assistant
 #pragma warning disable MA0025
        throw new NotImplementedException();
 #pragma warning restore MA0025
    }
}
