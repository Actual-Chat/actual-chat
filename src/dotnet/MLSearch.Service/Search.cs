using ActualChat.Chat;
using ActualChat.Search;
using ActualChat.Users;

#pragma warning disable MA0049
namespace ActualChat.MLSearch;
public class Search(ISearchBackend backend, IAccounts accounts, IPlaces places, IChats chats) : ISearch
{
    // Not a [ComputeMethod]!
    public virtual async Task<ContactSearchResultPage> FindContacts(
        Session session,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        var ownAccount = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!ownAccount.IsActive())
            return ContactSearchResultPage.Empty;

        if (query.MustFilterByPlace) {
            var place = await places.Get(session, query.PlaceId.Value, cancellationToken).ConfigureAwait(false);
            if (place is null)
                return ContactSearchResultPage.Empty;
        }

        return await backend.FindContacts(ownAccount.Id, query, cancellationToken).ConfigureAwait(false);
    }

    // Not a [ComputeMethod]!
    public virtual async Task<EntrySearchResultPage> FindEntries(
        Session session,
        EntrySearchQuery query,
        CancellationToken cancellationToken)
    {
        var ownAccount = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!ownAccount.IsActive())
            return EntrySearchResultPage.Empty;

        if (!query.ChatId.IsNone) {
            var chat = await chats.Get(session, query.ChatId, cancellationToken).ConfigureAwait(false);
            if (chat is null)
                return EntrySearchResultPage.Empty;
        } else if (query.PlaceId is { IsNone: false } placeId) {
            var place = await places.Get(session, placeId, cancellationToken).ConfigureAwait(false);
            if (place is null)
                return EntrySearchResultPage.Empty;
        }

        return await backend.FindEntries(ownAccount.Id, query, cancellationToken).ConfigureAwait(false);
    }
}
