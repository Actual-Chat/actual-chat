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
        => FindContacts(tester, ContactSearchScope.People, isExistingContact, criteria, limit, placeId);


    public static Task<ApiArray<ContactSearchResult>> FindGroups(
        this IWebTester tester,
        string criteria,
        bool own,
        PlaceId? placeId = null,
        int? limit = null)
        => FindContacts(tester, ContactSearchScope.Groups, own, criteria, limit, placeId);


    public static Task<ApiArray<ContactSearchResult>> FindPlaces(
        this IWebTester tester,
        string criteria,
        bool own,
        int? limit = null)
        => FindContacts(tester, ContactSearchScope.Places, own, criteria, limit);


    public static async Task<ApiArray<EntrySearchResult>> FindEntries(
        this IWebTester tester,
        string criteria,
        PlaceId? placeId = null,
        ChatId chatId = default,
        int? limit = null)
    {
        var response = await tester.Search.FindEntries(tester.Session, new EntrySearchQuery {
            Criteria = criteria,
            Limit = limit ?? 20,
            PlaceId = placeId,
            ChatId = chatId,
        }, CancellationToken.None);
        return response.Hits;
    }

    private static async Task<ApiArray<ContactSearchResult>> FindContacts(
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
