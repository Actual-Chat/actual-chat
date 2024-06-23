using ActualChat.Search;

namespace ActualChat.Testing.Host;

public static class SearchOperations
{
    public static Task<ApiArray<ContactSearchResult>> FindPeople(
        this IWebTester tester,
        string criteria,
        bool isExistingContact,
        PlaceId? placeId = null,
        int? limit = null)
        => Find(tester, ContactSearchScope.People, isExistingContact, criteria, limit, placeId);


    public static Task<ApiArray<ContactSearchResult>> FindGroups(
        this IWebTester tester,
        string criteria,
        bool own,
        PlaceId? placeId = null,
        int? limit = null)
        => Find(tester, ContactSearchScope.Groups, own, criteria, limit, placeId);


    public static Task<ApiArray<ContactSearchResult>> FindPlaces(
        this IWebTester tester,
        string criteria,
        bool own,
        int? limit = null)
        => Find(tester, ContactSearchScope.Places, own, criteria, limit);

    private static async Task<ApiArray<ContactSearchResult>> Find(
        IWebTester tester,
        ContactSearchScope scope,
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
