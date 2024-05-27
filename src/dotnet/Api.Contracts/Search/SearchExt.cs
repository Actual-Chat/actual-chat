using ActualChat.Contacts;

namespace ActualChat.Search;

public static class SearchExt
{
    public static Task<ApiArray<ContactSearchResult>> FindChatContacts(
        this ISearch search,
        Session session,
        string criteria,
        bool isPublic,
        CancellationToken cancellationToken = default)
        => search.FindChatContacts(session, null, criteria, isPublic, cancellationToken);

    public static async Task<ApiArray<ContactSearchResult>> FindChatContacts(
        this ISearch search,
        Session session,
        PlaceId? placeId,
        string criteria,
        bool isPublic,
        CancellationToken cancellationToken = default)
    {
        var results = await search.FindContacts(session,
                new ContactSearchQuery {
                    Criteria = criteria,
                    Kind = ContactKind.Chat,
                    IsPublic = isPublic,
                    PlaceId = placeId,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return results.Hits;
    }

    public static Task<ApiArray<ContactSearchResult>> FindUserContacts(
        this ISearch search,
        Session session,
        string criteria,
        CancellationToken cancellationToken = default)
        => search.FindUserContacts(session, null, criteria, cancellationToken);

    public static async Task<ApiArray<ContactSearchResult>> FindUserContacts(
        this ISearch search,
        Session session,
        PlaceId? placeId,
        string criteria,
        CancellationToken cancellationToken = default)
    {
        var results = await search.FindContacts(session,
                new ContactSearchQuery {
                    Criteria = criteria,
                    Kind = ContactKind.User,
                    PlaceId = placeId,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return results.Hits;
    }
}
