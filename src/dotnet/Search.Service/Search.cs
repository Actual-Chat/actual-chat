using ActualChat.Chat;
using ActualChat.Users;

#pragma warning disable MA0049
namespace ActualChat.Search;
public class Search(ISearchBackend backend, IAccounts accounts, IPlaces places) : ISearch
{
    public virtual async Task<ContactSearchResultPage> FindContacts(
        Session session,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        var ownAccount = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!ownAccount.IsActive())
            return ContactSearchResultPage.Empty;

        if (query.PlaceId != null && query.PlaceId != PlaceId.None) {
            var place = await places.Get(session, query.PlaceId.Value, cancellationToken).ConfigureAwait(false);
            if (place is null)
                return ContactSearchResultPage.Empty;
        }

        return await backend.FindContacts(ownAccount.Id,
                query,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
