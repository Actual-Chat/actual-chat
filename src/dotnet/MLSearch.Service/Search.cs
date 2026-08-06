using ActualChat.Search;

/// <summary>
/// Frontend service for AI-powered search across contacts and chat entries.
/// </summary>
#pragma warning disable MA0049
namespace ActualChat.MLSearch;
public class Search(ISearchBackend backend, IAccounts accounts, IPlaces places, IChats chats) : ISearch
{
    // Not a [ComputeMethod]!
    public virtual async Task<SearchResult<FoundContact>> FindContacts(
        Session session,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        var ownAccount = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!ownAccount.IsActive())
            return SearchResult<FoundContact>.Empty;

        if (query.MustFilterByPlace) {
            var place = await places.Get(session, query.PlaceId, cancellationToken).ConfigureAwait(false);
            if (place is null)
                return SearchResult<FoundContact>.Empty;
        }

        return await backend.FindContacts(ownAccount.Id, query, cancellationToken).ConfigureAwait(false);
    }

    // Not a [ComputeMethod]!
    public virtual async Task<SearchResult<FoundChatEntry>> FindEntries(
        Session session,
        EntrySearchQuery query,
        CancellationToken cancellationToken)
    {
        var ownAccount = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!ownAccount.IsActive())
            return SearchResult<FoundChatEntry>.Empty;

        if (query.ChatId is { } chatId) {
            var chat = await chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat is null)
                return SearchResult<FoundChatEntry>.Empty;
        } else if (query.PlaceId is { } placeId) {
            var place = await places.Get(session, placeId, cancellationToken).ConfigureAwait(false);
            if (place is null)
                return SearchResult<FoundChatEntry>.Empty;
        }

        return await backend.FindEntries(ownAccount.Id, query, cancellationToken).ConfigureAwait(false);
    }
}
