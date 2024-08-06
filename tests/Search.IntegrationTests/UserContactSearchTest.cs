using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.Users;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection))]
[Trait("Category", "Slow")]
public class UserContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedDbLocalAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private ICommander _commander = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewWebClientTester(Out);
        _commander = AppHost.Services.Commander();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldNotFindFriendsIfNotInContacts()
    {
        // arrange
        await _tester.SignInAsAlice();
        var accounts = await _tester.CreateAccounts(10);
        await _tester.SignInAsUniqueBob();

        // act
        var updates = BuildUserContacts(accounts);
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(true));

        // act
        var searchResults = await _tester.FindPeople("User", true);

        // assert
        searchResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotFindOtherUserContactsIfAllInContacts()
    {
        // arrange
        await _tester.SignInAsAlice();
        var accounts = await _tester.CreateAccounts(10);
        var bob = await _tester.SignInAsUniqueBob();
        foreach (var other in accounts)
            await _tester.CreatePeerContact(bob, other);

        // act
        var updates = BuildUserContacts(accounts);
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(true));

        // act
        var searchResults = await _tester.FindPeople("User", false);

        // assert
        searchResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldFindUsers()
    {
        // arrange
        await _tester.SignInAsAlice();
        var accounts = await _tester.CreateAccounts(10);
        var bob = await _tester.SignInAsUniqueBob();
        for (int i = 0; i < 5; i++)
            await _tester.CreatePeerContact(bob, accounts[i]);

        // act
        var updates = BuildUserContacts(accounts);
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, ApiArray<IndexedUserContact>.Empty));
        await _commander.Call(new SearchBackend_Refresh(true));

        // act
        var searchResults = await _tester.FindPeople("User", true);

        // assert
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[..5]), o => o.ExcludingSearchMatch());

        // act
        searchResults = await _tester.FindPeople("User", false);

        // assert
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[5..]), o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindByPrefix()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob);
        var people = await _tester.CreateUserContacts(bob, places);

        // act
        var updates = people.ToIndexedUserContacts(places).ToApiArray();
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(true));
        await _tester.SignIn(bob);

        // act
        var searchResults = await _tester.FindPeople("us", true);

        // assert
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(people.Friends().ToArray()), o => o.ExcludingSearchMatch());

        // act
        searchResults = await _tester.FindPeople("us", false);

        // assert
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(people.Strangers().ToArray()), o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindByPrefixInPlace()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob);
        var people = await _tester.CreateUserContacts(bob, places);

        // act
        var updates = people.ToIndexedUserContacts(places).ToApiArray();
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(true));
        await _tester.SignIn(bob);

        // act
        var searchResults = await _tester.FindPeople("us", true, places.JoinedPublicPlace1().Id);

        // assert
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(people.Friend1FromPublicPlace1(), people.Friend2FromPublicPlace1()),
                o => o.ExcludingSearchMatch());

        // act
        searchResults = await _tester.FindPeople("us", false, places.JoinedPublicPlace1().Id);

        // assert
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(people.Stranger1FromPublicPlace1(), people.Stranger2FromPublicPlace1()),
                o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindUpdatedUsers()
    {
        // arrange
        await _tester.SignInAsAlice();
        var accounts = await _tester.CreateAccounts(10);
        var bob = await _tester.SignInAsUniqueBob();
        for (int i = 0; i < 5; i++)
            await _tester.CreatePeerContact(bob, accounts[i]);

        // act
        var updates = BuildUserContacts(accounts);
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, ApiArray<IndexedUserContact>.Empty));
        await _commander.Call(new SearchBackend_Refresh(true));

        // assert
        var searchResults = await _tester.FindPeople("User", true);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[..5]), o => o.ExcludingSearchMatch());

        // assert
        searchResults = await _tester.FindPeople("User", false);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[5..]), o => o.ExcludingSearchMatch());

        // act
        updates = BuildUserContacts(accounts[4] with { Name = "aaa" }, accounts[9] with { Name = "aaa" });
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(true));

        // assert
        searchResults = await _tester.FindPeople("User", true);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[..4]), o => o.ExcludingSearchMatch());

        // assert
        searchResults = await _tester.FindPeople("User", false);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[5..9]), o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldNotFindDeletedUsers()
    {
        // arrange
        await _tester.SignInAsAlice();
        var accounts = await _tester.CreateAccounts(10);
        var bob = await _tester.SignInAsUniqueBob();
        for (int i = 0; i < 5; i++)
            await _tester.CreatePeerContact(bob, accounts[i]);

        // act
        var updates = BuildUserContacts(accounts);
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, ApiArray<IndexedUserContact>.Empty));
        await _commander.Call(new SearchBackend_Refresh(true));

        // assert
        var searchResults = await _tester.FindPeople("User", true);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[..5]), o => o.ExcludingSearchMatch());

        // assert
        searchResults = await _tester.FindPeople("User", false);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[5..]), o => o.ExcludingSearchMatch());

        // act
        var deleted = BuildUserContacts(accounts[4], accounts[9]);
        await _commander.Call(new SearchBackend_UserContactBulkIndex([], deleted));
        await _commander.Call(new SearchBackend_Refresh(true));

        // assert
        searchResults = await _tester.FindPeople("User", true);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[..4]), o => o.ExcludingSearchMatch());

        // assert
        searchResults = await _tester.FindPeople("User", false);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[5..9]), o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindOnlPlaceMembers()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob, 1);
        var people = await _tester.CreateUserContacts(bob, places);
        await _tester.SignIn(bob);

        // act
        var updates = people.Select(x => x.Value.ToIndexedUserContact(places[x.Key.PlaceKey].Id)).ToApiArray();
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(true));

        // assert
        var searchResults = await _tester.FindPeople("one", true, places.JoinedPrivatePlace1().Id);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(people.Friend1FromPrivatePlace1(), people.Friend2FromPrivatePlace1()),
                o => o.ExcludingSearchMatch());

        // assert
        searchResults = await _tester.FindPeople("one", false, places.JoinedPrivatePlace1().Id);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(people.Stranger1FromPrivatePlace1(), people.Stranger2FromPrivatePlace1()),
                o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldTakeOnlyFromSpecifiedPlace()
    {
        // arrange
        var friendFromPlace1 = await _tester.CreateAccount("Place 1 member - Bob's friend");
        var strangerFromPlace1 = await _tester.CreateAccount("Place 1 member - Not Bob's friend");
        var friendFromPlace2 = await _tester.CreateAccount("Place 2 member - Bob's friend");
        var strangerFromPlace2 = await _tester.CreateAccount("Place 2 member - Not Bob's friend");
        var friendFromBothPlaces = await _tester.CreateAccount("Both places member - Bob's friend");
        var strangerFromBothPlaces = await _tester.CreateAccount("Both places member - Not Bob's friend");
        var bob = await _tester.SignInAsBob();
        await _tester.SignInAsAlice();
        var place1 = await _tester.CreatePlace(false, usersToInvite: bob);
        var place2 = await _tester.CreatePlace(false, usersToInvite: bob);
        await _tester.SignIn(bob);
        await _tester.CreatePeerContacts(bob, friendFromPlace1, friendFromPlace2, friendFromBothPlaces);

        // act
        var updates = ApiArray.New(
            friendFromPlace1.ToIndexedUserContact(place1.Id),
            strangerFromPlace1.ToIndexedUserContact(place1.Id),
            friendFromPlace2.ToIndexedUserContact(place2.Id),
            strangerFromPlace2.ToIndexedUserContact(place2.Id),
            friendFromBothPlaces.ToIndexedUserContact(place1.Id, place2.Id),
            strangerFromBothPlaces.ToIndexedUserContact(place1.Id, place2.Id));
        await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(true));
        await _tester.SignIn(bob);
        var searchResults = await _tester.FindPeople("member", true, place2.Id);

        // assert
        searchResults.Should()
            .BeEquivalentTo([
                    bob.BuildSearchResult(friendFromPlace2, [(8, 14)]),
                    bob.BuildSearchResult(friendFromBothPlaces, [(12, 18)]),
                ],
                o => o.ExcludingRank());

        // act
        searchResults = await _tester.FindPeople("member", false, place2.Id);

        // assert
        searchResults.Should()
            .BeEquivalentTo([
                    bob.BuildSearchResult(strangerFromPlace2, [(8, 14)]),
                    bob.BuildSearchResult(strangerFromBothPlaces, [(12, 18)])
                ],
                o => o.ExcludingRank());
    }

    // Private methods

    private static ApiArray<IndexedUserContact> BuildUserContacts(params AccountFull[] accounts)
        => accounts.Select(x => x.ToIndexedUserContact()).ToApiArray();
}
