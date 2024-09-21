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
                var chatIds = await ListGroupsForContactSearch(SearchScope.Groups, ct);
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
                var foundPeerChats = await ListGroupsForContactSearch(SearchScope.People, ct);
                var expected = accounts[..5]
                    .Select(x => new Chat.Chat(new PeerChatId(bob.Id, x.Id).ToChatId()) {
                        Title = x.FullName,
                    });
                foundPeerChats.Should().BeEquivalentTo(expected, o => o.IdTitle());
            },
            TimeSpan.FromSeconds(10));
    }

    private async Task<List<Chat.Chat>> ListGroupsForContactSearch(SearchScope scope, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetOwn(_tester.Session, cancellationToken);
        var contactIds = scope == SearchScope.People
            ? await _contactsBackend.ListIdsForUserContactSearch(account.Id, cancellationToken)
            : await _contactsBackend.ListIdsForGroupContactSearch(account.Id, null, cancellationToken);
        var chats = await contactIds.Where(x => !Constants.Chat.SystemChatIds.Contains(x.ChatId))
            .OrderBy(x => x.Id)
            .Select(x => x.ChatId)
            .Select(id => _tester.Chats.Get(_tester.Session, id, cancellationToken))
            .Collect(cancellationToken);
        return chats.SkipNullItems().ToList();
    }
}
