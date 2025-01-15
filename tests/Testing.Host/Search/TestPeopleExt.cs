using ActualChat.Chat;
using ActualChat.MLSearch;
using ActualChat.Users;
using IndexedUserContact = ActualChat.MLSearch.Documents.IndexedUserContact;

namespace ActualChat.Testing.Host;

public static class TestPeopleExt
{
    public static AccountFull Friend1FromPublicPlace1(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Friend1(TestPlaceKey.JoinedPublicPlace1)];
    public static AccountFull Friend1FromPublicPlace2(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Friend1(TestPlaceKey.JoinedPublicPlace2)];
    public static AccountFull Friend1FromPrivatePlace1(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Friend1(TestPlaceKey.JoinedPrivatePlace1)];
    public static AccountFull Friend1FromPrivatePlace2(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Friend1(TestPlaceKey.JoinedPrivatePlace2)];
    public static AccountFull Friend2FromPublicPlace1(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Friend2(TestPlaceKey.JoinedPublicPlace1)];
    public static AccountFull Friend2FromPublicPlace2(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Friend2(TestPlaceKey.JoinedPublicPlace2)];
    public static AccountFull Friend2FromPrivatePlace1(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Friend2(TestPlaceKey.JoinedPrivatePlace1)];
    public static AccountFull Friend2FromPrivatePlace2(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Friend2(TestPlaceKey.JoinedPrivatePlace2)];
    public static AccountFull Stranger1FromPublicPlace1(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Stranger1(TestPlaceKey.JoinedPublicPlace1)];
    public static AccountFull Stranger1FromPublicPlace2(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Stranger1(TestPlaceKey.JoinedPublicPlace2)];
    public static AccountFull Stranger1FromPrivatePlace1(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Stranger1(TestPlaceKey.JoinedPrivatePlace1)];
    public static AccountFull Stranger1FromPrivatePlace2(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Stranger1(TestPlaceKey.JoinedPrivatePlace2)];
    public static AccountFull Stranger2FromPublicPlace1(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Stranger2(TestPlaceKey.JoinedPublicPlace1)];
    public static AccountFull Stranger2FromPublicPlace2(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Stranger2(TestPlaceKey.JoinedPublicPlace2)];
    public static AccountFull Stranger2FromPrivatePlace1(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Stranger2(TestPlaceKey.JoinedPrivatePlace1)];
    public static AccountFull Stranger2FromPrivatePlace2(this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people[TestChatKey.Stranger2(TestPlaceKey.JoinedPrivatePlace2)];

    public static IEnumerable<AccountFull> Friends1(this IReadOnlyDictionary<TestChatKey, AccountFull> people) => [
        people.Friend1FromPublicPlace1(),
        people.Friend1FromPublicPlace2(),
        people.Friend1FromPrivatePlace1(),
        people.Friend1FromPrivatePlace2(),
    ];

    public static IEnumerable<AccountFull> Strangers1(this IReadOnlyDictionary<TestChatKey, AccountFull> people) => [
        people.Stranger1FromPublicPlace1(),
        people.Stranger1FromPublicPlace2(),
        people.Stranger1FromPrivatePlace1(),
        people.Stranger1FromPrivatePlace2(),
    ];

    public static IEnumerable<AccountFull> Friends(
        this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people.Where(x => x.Key.MustJoin).Select(x => x.Value);

    public static IEnumerable<AccountFull> Strangers(
        this IReadOnlyDictionary<TestChatKey, AccountFull> people)
        => people.Where(x => !x.Key.MustJoin).Select(x => x.Value);

    public static IEnumerable<IndexedUserContact> ToIndexedUserContacts(
        this IReadOnlyDictionary<TestChatKey, AccountFull> people,
        IReadOnlyDictionary<TestPlaceKey, Place> places)
    {
        return people.Select(x => x.Value.ToIndexedUserContact(GetPlaces(x)));

        PlaceId[] GetPlaces(KeyValuePair<TestChatKey, AccountFull> pair)
        {
            if (pair.Key.PlaceKey is null)
                return [];

            var place = places.GetValueOrDefault(pair.Key.PlaceKey);
            if (place == null)
                return [];

            return [place.Id];
        }
    }
}
