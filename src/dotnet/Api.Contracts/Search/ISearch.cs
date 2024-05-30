namespace ActualChat.Search;

public interface ISearch : IComputeService
{
    Task<ContactSearchResultPage> FindContacts(
        Session session,
        ContactSearchQuery query,
        CancellationToken cancellationToken);
}
