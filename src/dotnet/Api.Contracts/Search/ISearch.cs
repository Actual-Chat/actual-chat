namespace ActualChat.Search;

public interface ISearch : IComputeService
{
    // Non-compute methods
    Task<ContactSearchResultPage> FindContacts(
        Session session,
        ContactSearchQuery query,
        CancellationToken cancellationToken);

    Task<EntrySearchResultPage> FindEntries(
        Session session,
        EntrySearchQuery query,
        CancellationToken cancellationToken);
}
