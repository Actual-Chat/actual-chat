using ActualChat.Search;

namespace ActualChat.Testing.Host;

public static class SearchOperations
{
    public static Task<ContactSearchResult[]> FindPeople(
        this IWebTester tester,
        string criteria,
        bool isExistingContact,
        PlaceId? placeId = null,
        int? limit = null)
        => FindContacts(tester, SearchScope.People, isExistingContact, criteria, limit, placeId);


    public static Task<ContactSearchResult[]> FindGroups(
        this IWebTester tester,
        string criteria,
        bool own,
        PlaceId? placeId = null,
        int? limit = null)
        => FindContacts(tester, SearchScope.Groups, own, criteria, limit, placeId);


    public static Task<ContactSearchResult[]> FindPlaces(
        this IWebTester tester,
        string criteria,
        bool own,
        int? limit = null)
        => FindContacts(tester, SearchScope.Places, own, criteria, limit);


    public static async Task<EntrySearchResult[]> FindEntries(
        this IWebTester tester,
        string criteria,
        PlaceId? placeId = null,
        ChatId chatId = default,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var response = await tester.Search.FindEntries(tester.Session, new EntrySearchQuery {
            Criteria = criteria,
            Limit = limit ?? 50,
            PlaceId = placeId,
            ChatId = chatId,
        }, cancellationToken);
        return response.Hits;
    }

    private static async Task<ContactSearchResult[]> FindContacts(
        IWebTester tester,
        SearchScope scope,
        bool own,
        string criteria,
        int? limit,
        PlaceId? placeId = null)
    {
        var results = await tester.Search.FindContacts(tester.Session,
            new () {
                Criteria = criteria,
                Own = own,
                Scope = scope,
                Limit = limit ?? 20,
                PlaceId = placeId,
            },
            CancellationToken.None);
        results.Offset.Should().Be(0);
        return results.Hits;
    }
}
