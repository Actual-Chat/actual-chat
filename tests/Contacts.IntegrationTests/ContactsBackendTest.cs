using ActualChat.Search;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.Users;

namespace ActualChat.Contacts.IntegrationTests;

[Collection(nameof(ContactCollection))]
public class ContactsBackendTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private IContactsBackend _contactsBackend = null!;
    private IAccounts _accounts = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewWebClientTester(Out);
        _contactsBackend = AppHost.Services.GetRequiredService<IContactsBackend>();
        _accounts = _tester.AppServices.GetRequiredService<IAccounts>();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldListOwnGroupContactIds()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob, 1);
        var chats = await _tester.CreateGroupContacts(bob, places, nonPlaceChatIndexCount: 1, placeChatIndexCount: 1);

        // act
        await _tester.SignIn(bob);

        // assert
        var expected = chats.JoinedGroups1();
        await ComputedTest.When(async ct => {
                var chatIds = await ListChatsForContactSearch(SearchScope.Groups, ct);
                chatIds.Should().BeEquivalentTo(expected, o => o.IdTitle());
            },
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ShouldListOwnUserContactIds()
    {
        // arrange
        await _tester.SignInAsAlice();
        var accounts = await _tester.CreateAccounts(10);
        var bob = await _tester.SignInAsUniqueBob();
        for (int i = 0; i < 5; i++)
            await _tester.CreatePeerContact(bob, accounts[i]);

        // act
        await _tester.SignIn(bob);

        // assert
        await ComputedTest.When(async ct => {
                var foundPeerChats = await ListChatsForContactSearch(SearchScope.People, ct);
                var expected = accounts[..5]
                    .Select(x => new Chat.Chat(new PeerChatId(bob.Id, x.Id).ToChatId()) {
                        Title = x.FullName,
                    });
                foundPeerChats.Should().BeEquivalentTo(expected, o => o.IdTitle());
            },
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ShouldListIdsForSearch()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();

        // act
        await _tester.SignInAsUniqueAlice();
        var places = await _tester.CreatePlaceContacts(bob);
        var groups = await _tester.CreateGroupContacts(bob, places);
        var people = await _tester.CreateUserContacts(bob, places);
        await _tester.SignIn(bob);

        // act, assert
        // non-place results
        await TestListIdsForSearch(PlaceId.None, false, groups.NonPlacePrivateJoined(), people.Friends());
        await TestListIdsForSearch(PlaceId.None, true, groups.NonPlaceJoined(), people.Friends());

        // found everywhere
        await TestListIdsForSearch(null, false, groups.PrivateJoined(), people.Friends());
        await TestListIdsForSearch(null, true, groups.Joined(), people.Friends());

        // place bound results
        foreach (var (placeKey, place) in places) {
            await TestListIdsForSearch(place.Id, false, groups.PrivateJoinedInPlace(placeKey), []);
            await TestListIdsForSearch(place.Id, true, groups.VisibleInPlace(placeKey), []);
        }

        return;

        async Task TestListIdsForSearch(PlaceId? placeId, bool includePublic, IEnumerable<Chat.Chat> expectedGroups, IEnumerable<AccountFull> expectedUsers)
        {
            // arrange
            var expected = ExpectedIds(expectedGroups, expectedUsers);

            // act
            var contactIds = await ListIdsForSearch(placeId, includePublic, expected.Count);

            // assert
            contactIds.Order().Should().Equal(expected);
        }

        List<ContactId> ExpectedIds(IEnumerable<Chat.Chat> expectedGroups, IEnumerable<AccountFull> expectedUsers)
            => expectedUsers.Select(x => new PeerChatId(bob.Id, x.Id).ToChatId())
                .Concat(expectedGroups.Select(x => x.Id))
                .Select(x => new ContactId(bob.Id, x))
                .Order()
                .ToList();
    }

    private async Task<List<Chat.Chat>> ListChatsForContactSearch(SearchScope scope, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetOwn(_tester.Session, cancellationToken);
        var contactIds = scope == SearchScope.People
            ? await _contactsBackend.ListPeerContactIds(account.Id, cancellationToken)
            : await _contactsBackend.ListIdsForGroupContactSearch(account.Id, null, cancellationToken);
        var chats = await contactIds.Where(x => !Constants.Chat.SystemChatIds.Contains(x.ChatId))
            .OrderBy(x => x.Id)
            .Select(x => x.ChatId)
            .Select(id => _tester.Chats.Get(_tester.Session, id, cancellationToken))
            .Collect(cancellationToken);
        return chats.SkipNullItems().ToList();
    }

    private async Task<List<ContactId>> ListIdsForSearch(PlaceId? placeId, bool includePublic, int expectedCount)
    {
        var account = await _accounts.GetOwn(_tester.Session, CancellationToken.None);
        return await ComputedTest.When(async ct => {
                var contactIds =
                    await _contactsBackend.ListIdsForSearch(account.Id, placeId, includePublic, ct);
                var result = contactIds.Where(x => !Constants.Chat.SystemChatIds.Contains(x.ChatId))
                    .OrderBy(x => x.Id)
                    .ToList();
                result.Should().HaveCount(expectedCount);

                return result;
            },
            TimeSpan.FromSeconds(5));
    }
}
