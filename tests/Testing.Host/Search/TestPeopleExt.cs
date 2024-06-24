using ActualChat.Users;

namespace ActualChat.Testing.Host;

public static class TestPeopleExt
{
    public static AccountFull Friend1FromPublicPlace1(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(0, true, true), 0, true)];
    public static AccountFull Friend1FromPublicPlace2(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(1, true, true), 0, true)];
    public static AccountFull Friend1FromPrivatePlace1(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(0, false, true), 0, true)];
    public static AccountFull Friend1FromPrivatePlace2(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(1, false, true), 0, true)];
    public static AccountFull Friend2FromPublicPlace1(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(0, true, true), 1, true)];
    public static AccountFull Friend2FromPublicPlace2(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(1, true, true), 1, true)];
    public static AccountFull Friend2FromPrivatePlace1(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(0, false, true), 1, true)];
    public static AccountFull Friend2FromPrivatePlace2(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(1, false, true), 1, true)];
    public static AccountFull Stranger1FromPublicPlace1(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(0, true, true), 0, false)];
    public static AccountFull Stranger1FromPublicPlace2(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(1, true, true), 0, false)];
    public static AccountFull Stranger1FromPrivatePlace1(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(0, false, true), 0, false)];
    public static AccountFull Stranger1FromPrivatePlace2(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(1, false, true), 0, false)];
    public static AccountFull Stranger2FromPublicPlace1(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(0, true, true), 1, false)];
    public static AccountFull Stranger2FromPublicPlace2(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(1, true, true), 1, false)];
    public static AccountFull Stranger2FromPrivatePlace1(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(0, false, true), 1, false)];
    public static AccountFull Stranger2FromPrivatePlace2(this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people[new (new TestPlaceKey(1, false, true), 1, false)];

    public static IEnumerable<AccountFull> Friends1(this IReadOnlyDictionary<TestUserKey, AccountFull> people) => [
        people.Friend1FromPublicPlace1(),
        people.Friend1FromPublicPlace2(),
        people.Friend1FromPrivatePlace1(),
        people.Friend1FromPrivatePlace2(),
    ];

    public static IEnumerable<AccountFull> Strangers1(this IReadOnlyDictionary<TestUserKey, AccountFull> people) => [
        people.Stranger1FromPublicPlace1(),
        people.Stranger1FromPublicPlace2(),
        people.Stranger1FromPrivatePlace1(),
        people.Stranger1FromPrivatePlace2(),
    ];

    public static IEnumerable<AccountFull> Friends(
        this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people.Where(x => x.Key.IsExistingContact).Select(x => x.Value);

    public static IEnumerable<AccountFull> Strangers(
        this IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => people.Where(x => !x.Key.IsExistingContact).Select(x => x.Value);
}
