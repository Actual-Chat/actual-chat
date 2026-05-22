namespace ActualChat.Search;

/// <summary>
/// Service for searching contacts and chat entries.
/// </summary>
public interface ISearch : IComputeService
{
    // Non-compute methods
    Task<SearchResult<FoundContact>> FindContacts(
        Session session,
        ContactSearchQuery query,
        CancellationToken cancellationToken);

    Task<SearchResult<FoundChatEntry>> FindEntries(
        Session session,
        EntrySearchQuery query,
        CancellationToken cancellationToken);
}
