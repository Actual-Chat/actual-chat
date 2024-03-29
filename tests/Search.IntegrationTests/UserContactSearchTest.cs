using ActualChat.Performance;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection))]
[Trait("Category", "Slow")]
public class UserContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private ISearchBackend _sut = null!;
    private ICommander _commander = null!;

    private static readonly AccountFull Jack = new (new User(UserId.New(), "Jack")) {
        Name = "Jack",
        LastName = "Stone",
    };
    private static readonly AccountFull Rebecca = new (new User(UserId.New(), "Rebecca")) {
        Name = "Rebecca",
        LastName = "Scissors",
    };
    private static readonly AccountFull Luke = new (new User(UserId.New(), "Luke")) {
        Name = "Luke",
        LastName = "Paper",
    };
    private static readonly AccountFull Olivia = new (new User(UserId.New(), "Olivia")) {
        Name = "Olivia",
        LastName = "Green",
    };
    private static readonly AccountFull Aaron = new (new User(UserId.New(), "Aaron")) {
        Name = "Aaron",
        LastName = "Sky",
    };
    private static readonly AccountFull Emma = new (new User(UserId.New(), "Emma")) {
        Name = "Emma",
        LastName = "Blue",
    };
    private static readonly AccountFull Emily = new (new User(UserId.New(), "Emily")) {
        Name = "Emily",
        LastName = "Yellow",
    };
    private static readonly AccountFull Camila = new (new User(UserId.New(), "Camila")) {
        Name = "Camila",
        LastName = "Lake",
    };

    protected override Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        _tester = AppHost.NewWebClientTester(Out);
        _sut = AppHost.Services.GetRequiredService<ISearchBackend>();
        _commander = AppHost.Services.Commander();
        return Task.CompletedTask;
    }

    protected override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        await _tester.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task ShouldFindAddedUsers()
    {
        // arrange
        var bob = await _tester.SignInAsBob();
        var updates = BuildUserContacts(Jack,
            Rebecca,
            Luke,
            Olivia,
            Aaron,
            Emma,
            Camila,
            Emily);

        // act
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, ApiArray<IndexedUserContact>.Empty));
        await _commander.Call(new SearchBackend_Refresh(true));
        var searchResults = await Find(bob, "Ja");

        // assert
        searchResults.Should().BeEquivalentTo([BuildSearchResult(bob.Id, Jack)]);

        // act
        searchResults = await Find(bob, "Emily Yel");

        // assert
        searchResults.Should().BeEquivalentTo([BuildSearchResult(bob.Id, Emily)]);
    }

    private async Task<ApiArray<ContactSearchResult>> Find(AccountFull bob, string criteria)
    {
        var response = await _sut.FindUserContacts(bob.Id,
            criteria,
            0,
            20,
            CancellationToken.None);
        return response.Hits;
    }

    [Fact]
    public async Task ShouldFindUpdatedUsers()
    {
        // arrange
        var bob = await _tester.SignInAsBob();
        var updates = BuildUserContacts(Jack,
            Rebecca,
            Luke,
            Olivia,
            Aaron,
            Emma,
            Camila with { Name = "Ca1mila" },
            Emily);

        // act
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, ApiArray<IndexedUserContact>.Empty));
        await _commander.Call(new SearchBackend_Refresh(true));
        var searchResults = await Find(bob, "Camila");

        // assert
        searchResults.Should().BeEmpty();

        // act
        updates = ApiArray.New(BuildUserContact(Camila));
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, ApiArray<IndexedUserContact>.Empty));
        await _commander.Call(new SearchBackend_Refresh(true));
        searchResults = await Find(bob, "Camila");

        // assert
        searchResults.Should().BeEquivalentTo([BuildSearchResult(bob.Id, Camila)]);
    }

    [Fact]
    public async Task ShouldNotFindDeletedUsers()
    {
        // arrange
        var bob = await _tester.SignInAsBob();
        var updates = BuildUserContacts(Jack,
            Rebecca,
            Luke,
            Olivia,
            Aaron,
            Emma,
            Camila,
            Emily);

        // act
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, ApiArray<IndexedUserContact>.Empty));
        await _commander.Call(new SearchBackend_Refresh(true));
        var searchResults = await Find(bob, "em");

        // assert
        searchResults.Should()
            .BeEquivalentTo([
                BuildSearchResult(bob.Id, Emily),
                BuildSearchResult(bob.Id, Emma),
            ]);

        // act
        await _commander.Call(new SearchBackend_UserContactBulkIndex(ApiArray<IndexedUserContact>.Empty, BuildUserContacts(Emma)));
        await _commander.Call(new SearchBackend_Refresh(true));
        searchResults = await Find(bob, "em");

        // assert
        searchResults.Should().BeEquivalentTo([BuildSearchResult(bob.Id, Emily)]);
    }

    // Private methods

    private static ApiArray<IndexedUserContact> BuildUserContacts(params AccountFull[] accounts)
        => accounts.Select(BuildUserContact).ToApiArray();

    private static IndexedUserContact BuildUserContact(AccountFull account)
        => BuildUserContact(account.Id, account.Name, account.LastName);

    private static IndexedUserContact BuildUserContact(UserId userId, string name, string lastName)
        => new () {
            Id = userId,
            FullName = name + " " + lastName,
            FirstName = name,
            SecondName = lastName,
        };

    private static ContactSearchResult BuildSearchResult(UserId ownerId, AccountFull other)
        => BuildSearchResult(ownerId, other.Id, other.FullName);

    private static ContactSearchResult BuildSearchResult(UserId ownerId, UserId otherUserId, string fullName)
        => new (new ContactId(ownerId, new PeerChatId(ownerId, otherUserId).ToChatId()), SearchMatch.New(fullName));
}
