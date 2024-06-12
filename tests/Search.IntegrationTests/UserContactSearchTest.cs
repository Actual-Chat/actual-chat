using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection))]
public class UserContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private ISearch _sut = null!;
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
        LastName = "Emerson",
    };
    private static readonly AccountFull Emily = new (new User(UserId.New(), "Emily")) {
        Name = "Emily",
        LastName = "Yellow",
    };
    private static readonly AccountFull Camila = new (new User(UserId.New(), "Camila")) {
        Name = "Camila",
        LastName = "Lake",
    };

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        _tester = AppHost.NewWebClientTester(Out);
        _sut = AppHost.Services.GetRequiredService<ISearch>();
        _commander = AppHost.Services.Commander();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
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
        var searchResults = await Find("Ja");

        // assert
        searchResults.Should()
            .BeEquivalentTo([bob.BuildSearchResult(Jack, [(0, 4)])], o => o.ExcludingRank());

        // act
        searchResults = await Find("Emily Yel");

        // assert
        searchResults.Should()
            .BeEquivalentTo([bob.BuildSearchResult(Emily, [(0, 5), (6, 12)])], o => o.ExcludingRank());
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
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(true));
        var searchResults = await Find("Camila");

        // assert
        searchResults.Should().BeEmpty();

        // act
        updates = BuildUserContacts(Camila);
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(true));
        searchResults = await Find("Camila");

        // assert
        searchResults.Should().BeEquivalentTo([bob.BuildSearchResult(Camila, [(0, 6)])], o => o.ExcludingRank());
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
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(true));
        var searchResults = await Find("em");

        // assert
        searchResults.Should()
            .BeEquivalentTo([
                bob.BuildSearchResult(Emily, [(0, 5)]),
                bob.BuildSearchResult(Emma, [(0, 4), (5, 12)]),
            ], o => o.ExcludingRank());

        // act
        await _commander.Call(new SearchBackend_UserContactBulkIndex([], BuildUserContacts(Emma)));
        await _commander.Call(new SearchBackend_Refresh(true));
        searchResults = await Find("em");

        // assert
        searchResults.Should().BeEquivalentTo([bob.BuildSearchResult(Emily, [(0, 5)])], o => o.ExcludingRank());
    }

    [Fact]
    public async Task ShouldFindOnlyPlaceMembers()
    {
        // arrange
        var bob = await _tester.SignInAsBob();
        var (placeId, _) = await _tester.CreatePlace(false);
        await _tester.CreateAccounts(Jack,
            Rebecca,
            Luke,
            Olivia,
            Aaron,
            Emma,
            Camila,
            Emily);
        var updates = ApiArray.New(
            Jack.ToIndexedUserContact(),
            Rebecca.ToIndexedUserContact(placeId),
            Luke.ToIndexedUserContact(placeId),
            Olivia.ToIndexedUserContact(),
            Aaron.ToIndexedUserContact(),
            Emma.ToIndexedUserContact(placeId),
            Camila.ToIndexedUserContact(),
            Emily.ToIndexedUserContact(placeId));

        // act
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(true));
        var searchResults = await Find("em", placeId);

        // assert
        searchResults.Should()
            .BeEquivalentTo([
                    bob.BuildSearchResult(Emily, [(0, 5)]),
                    bob.BuildSearchResult(Emma, [(0, 4), (5, 12)]),
                ],
                o => o.ExcludingRank());

        // act
        searchResults = await Find("Emily Yel", placeId);

        // assert
        searchResults.Should().BeEquivalentTo([bob.BuildSearchResult(Emily, [(0, 5), (6, 12)])], o => o.ExcludingRank());
    }

    [Fact]
    public async Task ShouldTakeOnlyFromSpecifiedSpace()
    {
        // arrange
        var bob = await _tester.SignInAsBob();
        var (place1Id, _) = await _tester.CreatePlace(false);
        var (place2Id, _) = await _tester.CreatePlace(false);
        await _tester.CreateAccounts(Emily, Emma);
        var updates = ApiArray.New(
            Emily.ToIndexedUserContact(place1Id),
            Emma.ToIndexedUserContact(place1Id, place2Id));

        // act
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(true));
        var searchResults = await Find("em", place2Id);

        // assert
        searchResults.Should()
            .BeEquivalentTo([bob.BuildSearchResult(Emma, [(0, 4), (5, 12)])],
                o => o.ExcludingRank());
    }

    // Private methods

    private static ApiArray<IndexedUserContact> BuildUserContacts(params AccountFull[] accounts)
        => accounts.Select(x => x.ToIndexedUserContact()).ToApiArray();

    private Task<ApiArray<ContactSearchResult>> Find(string criteria, PlaceId? placeId = null)
        => _sut.FindUserContacts(_tester.Session, placeId, criteria);
}
