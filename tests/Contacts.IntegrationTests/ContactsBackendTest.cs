using ActualChat.Search;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;

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
                    .Select(x => new Chat.Chat(PeerChatId.New(bob.Id, x.Id)) {
                        Title = x.Name,
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
        await TestListIdsForSearch(ContactSubset.Chats(), false, groups.NonPlacePrivateJoined(), people.Friends());
        await TestListIdsForSearch(ContactSubset.Chats(), true, groups.NonPlaceJoined(), people.Friends());

        // found everywhere
        await TestListIdsForSearch(ContactSubset.All(), false, groups.PrivateJoined(), people.Friends());
        await TestListIdsForSearch(ContactSubset.All(), true, groups.Joined(), people.Friends());

        // place-bound results
        foreach (var (placeKey, place) in places) {
            await TestListIdsForSearch(ContactSubset.Place(place.Id), false, groups.PrivateJoinedInPlace(placeKey), []);
            await TestListIdsForSearch(ContactSubset.Place(place.Id), true, groups.VisibleInPlace(placeKey), []);
        }

        return;

        async Task TestListIdsForSearch(ContactSubset contactSubset, bool includePublic, IEnumerable<Chat.Chat> expectedGroups, IEnumerable<AccountFull> expectedUsers)
        {
            // arrange
            var expected = ExpectedIds(expectedGroups, expectedUsers);

            // act
            var contactIds = await ListIdsForSearch(contactSubset, includePublic, expected.Count);

            // assert
            contactIds.Order().Should().Equal(expected);
        }

        List<ContactId> ExpectedIds(IEnumerable<Chat.Chat> expectedGroups, IEnumerable<AccountFull> expectedUsers)
            => expectedUsers.Select(x => PeerChatId.New(bob.Id, x.Id))
                .Concat(expectedGroups.Select(x => x.Id))
                .Select(x => ContactId.NewAny(bob.Id, x))
                .Order()
                .ToList();
    }

    private async Task<List<Chat.Chat>> ListChatsForContactSearch(SearchScope scope, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetOwn(_tester.Session, cancellationToken);
        var contactIds = scope == SearchScope.People
            ? await _contactsBackend.ListPeerContactIds(account.Id, cancellationToken)
            : await _contactsBackend.ListIdsForGroupContactSearch(account.Id, ContactSubset.All(), cancellationToken);
        var chats = await contactIds
            .Where(x => !x.ChatId.IsSystem)
            .OrderBy(x => x.Id)
            .Select(x => x.ChatId)
            .Select(id => _tester.Chats.Get(_tester.Session, id, cancellationToken))
            .Collect(cancellationToken);
        return chats.SkipNullItems().ToList();
    }

    private async Task<List<ContactId>> ListIdsForSearch(ContactSubset contactSubset, bool includePublic, int expectedCount)
    {
        var account = await _accounts.GetOwn(_tester.Session, CancellationToken.None);
        return await ComputedTest.When(async ct => {
                var contactIds =
                    await _contactsBackend.ListIdsForSearch(account.Id, contactSubset, includePublic, ct);
                var result = contactIds.Where(x => !Constants.Chat.SystemChatIds.Contains(x.ChatId))
                    .OrderBy(x => x.Id)
                    .ToList();
                result.Should().HaveCount(expectedCount);

                return result;
            },
            TimeSpan.FromSeconds(5));
    }
}
