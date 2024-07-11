using ActualChat.Chat;
using ActualChat.Users;
using ITestGroupChatMap = System.Collections.Generic.IReadOnlyDictionary<ActualChat.Testing.Host.TestChatKey, ActualChat.Chat.Chat>;
using ITestPlaceMap = System.Collections.Generic.IReadOnlyDictionary<ActualChat.Testing.Host.TestPlaceKey, ActualChat.Chat.Place>;
using ITestUserMap = System.Collections.Generic.IReadOnlyDictionary<ActualChat.Testing.Host.TestUserKey, ActualChat.Users.AccountFull>;

namespace ActualChat.Testing.Host;

public static class TestContactUtil
{
    public static async Task<ITestPlaceMap> CreatePlaceContacts(
        this IWebTester tester,
        AccountFull contactOwner,
        int placeIndexCount = 2)
    {
        var places = new Dictionary<TestPlaceKey, Place>();
        for (int i = 0; i < placeIndexCount; i++)
            foreach (var isPublic in new[] { true, false })
            foreach (var mustJoin in new[] { true, false }) {
                var placeKey = new TestPlaceKey(i, isPublic, mustJoin);
                var place = await tester.CreatePlace(placeKey.IsPublic, GetPlaceTitle(contactOwner, placeKey));
                if (placeKey.MustJoin)
                    await tester.InviteToPlace(place.Id, contactOwner);
                places.Add(placeKey, place);
            }
        return places;
    }

    public static async Task<ITestGroupChatMap> CreateGroupContacts(this IWebTester tester, AccountFull contactOwner, ITestPlaceMap places, int nonPlaceChatIndexCount = 2, int placeChatIndexCount = 2)
    {
        var chats = new Dictionary<TestChatKey, Chat.Chat>();
        await GenerateChats(null, nonPlaceChatIndexCount, null);
        foreach (var (placeKey, place) in places)
            await GenerateChats(placeKey, placeChatIndexCount, place);

        return chats;

        string GetChatTitle(TestChatKey key)
            => $"{GetPlaceTitle(contactOwner, key.PlaceKey)} - {GetTitleChatPart(key)}";

        string GetTitleChatPart(TestChatKey key)
            => $"{GetVisibilityString(key.IsPublic)} chat {GetIndexString(key.Index)} {GetMembershipSuffix(contactOwner, key.MustJoin)}";

        async Task GenerateChats(TestPlaceKey? placeKey, int chatCount, Place? place)
        {
            foreach (var isPublic in new[] { true, false })
            foreach (var mustJoin in new[] { true, false })
                for (int i = 0; i < chatCount; i++) {
                    if (mustJoin && placeKey?.MustJoin == false)
                        continue; // impossible

                    if (placeKey?.MustJoin == true && !mustJoin && isPublic)
                        continue;

                    var key = new TestChatKey(placeKey, i, isPublic, mustJoin);
                    var (chat, _) = await tester.CreateAndGetChat(isPublic, GetChatTitle(key), place?.Id);
                    if (key.NeedsExplicitJoin)
                        await tester.InviteToChat(chat.Id, contactOwner);
                    chats.Add(key, chat);
                }
        }
    }

    public static async Task<ITestUserMap> CreateUserContacts(
        this IWebTester tester,
        AccountFull contactOwner,
        ITestPlaceMap places,
        int indexCount = 2)
    {
        var placeAdmin = await tester.GetOwnAccount();
        var users = new Dictionary<TestUserKey, AccountFull>();
        for (int i = 0; i < indexCount; i++)
            foreach (var isExistingContact in new[] { true, false })
            foreach (var isPlacePublic in new[] { true, false })
                for (int placeIndex = 0; placeIndex < places.Size(); placeIndex++) {
                    var placeKey = new TestPlaceKey(placeIndex, isPlacePublic, true);
                    var key = new TestUserKey(placeKey, i, isExistingContact);
                    var name = (isExistingContact ? "Friend" : "Stranger") + " User " + GetIndexString(i);
                    var lastName = "from " + GetPlaceTitle(contactOwner, placeKey);
                    var account = await tester.CreateAccount(name, lastName);
                    users.Add(key, account);
                }
        await CreatePeerContacts();
        await InviteToPlaces();
        return users;

        async Task InviteToPlaces()
        {
            await tester.SignIn(placeAdmin);
            foreach (var (key, account) in users)
                await tester.InviteToPlace(places[key.PlaceKey].Id, account);
        }

        async Task CreatePeerContacts()
        {
            await tester.SignIn(contactOwner);
            var friends = users.Where(x => x.Key.IsExistingContact).Select(x => x.Value).OfType<Account>().ToArray();
            await tester.CreatePeerContacts(contactOwner, friends);
        }
    }

    private static string GetPlaceTitle(AccountFull contactOwner, TestPlaceKey? key)
        => key == null
            ? "Non-place"
            : $"{GetVisibilityString(key.IsPublic)} place {GetIndexString(key.Index)} {GetMembershipSuffix(contactOwner, key.MustJoin)}";

    private static string GetVisibilityString(bool isPublic)
        => isPublic ? "public" : "private";

    private static string GetMembershipSuffix(AccountFull member, bool mustJoin)
        => (mustJoin ? "with" : "without") + $" {member.Name.NullIfEmpty() ?? "Bob"} as member";

    private static string GetIndexString(int index)
        => (index + 1).ToInvariantString() + " " + index switch { 0 => " one", 1 => "two", _ => "" };
}
