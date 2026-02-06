namespace ActualChat.Search;

/// <summary>
/// Service for searching contacts and chat entries.
/// </summary>
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
