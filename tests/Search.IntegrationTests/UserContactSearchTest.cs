using ActualChat.Chat;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.Users;
using Humanizer;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection))]
[Trait("Category", "Slow")]
public class UserContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private string UniquePart { get; } = UniqueNames.Prefix();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldNotFindFriendsIfNotInContacts()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var accounts = await CreateAccounts(10);
        await Tester.SignInAsUniqueBob();

        // act
        await Index(accounts);

        // act
        var searchResults = await Find("User", true);

        // assert
        searchResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotFindOtherUserContactsIfAllInContacts()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var accounts = await CreateAccounts(10);
        var bob = await Tester.SignInAsUniqueBob();
        foreach (var other in accounts)
            await Tester.CreatePeerContact(bob, other);

        // act
        await Index(accounts);

        // act
        var searchResults = await Find("User", false);

        // assert
        searchResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldFindUsers()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var accounts = await CreateAccounts(10);
        var bob = await Tester.SignInAsUniqueBob();
        for (int i = 0; i < 5; i++)
            await Tester.CreatePeerContact(bob, accounts[i]);

        // act
        await Index(accounts);

        // act
        var searchResults = await Find("User", true);

        // assert
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[..5]), o => o.ExcludingSearchMatch());

        // act
        searchResults = await Find("User", false);

        // assert
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[5..]), o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindByPrefix()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var people = await Tester.CreateUserContacts(bob, places, UniquePart);

        // act
        await Index(places, people);
        await Tester.SignIn(bob);

        // act
        var searchResults = await Find("us", true);

        // assert
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(people.Friends().ToArray()), o => o.ExcludingSearchMatch());

        // act
        searchResults = await Find("us", false);

        // assert
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(people.Strangers().ToArray()), o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindByPrefixInPlace()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var people = await Tester.CreateUserContacts(bob, places, UniquePart);

        // act
        await Index(places, people);
        await Tester.SignIn(bob);

        // act
        var searchResults = await Find("us", true, places.JoinedPublicPlace1().Id);

        // assert
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(people.Friend1FromPublicPlace1(), people.Friend2FromPublicPlace1()),
                o => o.ExcludingSearchMatch());

        // act
        searchResults = await Find("us", false, places.JoinedPublicPlace1().Id);

        // assert
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(people.Stranger1FromPublicPlace1(), people.Stranger2FromPublicPlace1()),
                o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindByMultipleWords()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var people = await Tester.CreateUserContacts(bob, places, UniquePart);

        // act
        await Index(places, people);
        await Tester.SignIn(bob);

        // act
        var searchResults = await Find("user tw", true);

        // assert
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(people.Friend1FromPublicPlace2(),
                    people.Friend1FromPrivatePlace2(),
                    people.Friend2FromPublicPlace1(),
                    people.Friend2FromPublicPlace2(),
                    people.Friend2FromPrivatePlace1(),
                    people.Friend2FromPrivatePlace2()),
                o => o.ExcludingSearchMatch());

        // act
        searchResults = await Find("user tw", false);

        // assert
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(people.Stranger1FromPublicPlace2(),
                    people.Stranger1FromPrivatePlace2(),
                    people.Stranger2FromPublicPlace1(),
                    people.Stranger2FromPublicPlace2(),
                    people.Stranger2FromPrivatePlace1(),
                    people.Stranger2FromPrivatePlace2()),
                o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindUpdatedUsers()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var accounts = await CreateAccounts(10);
        var bob = await Tester.SignInAsUniqueBob();
        for (int i = 0; i < 5; i++)
            await Tester.CreatePeerContact(bob, accounts[i]);

        // act
        await Index(accounts);

        // assert
        var searchResults = await Find("User", true);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[..5]), o => o.ExcludingSearchMatch());

        // assert
        searchResults = await Find("User", false);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[5..]), o => o.ExcludingSearchMatch());

        // act
        await Index([accounts[4] with { Name = "aaa" }, accounts[9] with { Name = "aaa" }]);

        // assert
        searchResults = await Find("User", true);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[..4]), o => o.ExcludingSearchMatch());

        // assert
        searchResults = await Find("User", false);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[5..9]), o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldNotFindDeletedUsers()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var accounts = await CreateAccounts(10);
        var bob = await Tester.SignInAsUniqueBob();
        for (int i = 0; i < 5; i++)
            await Tester.CreatePeerContact(bob, accounts[i]);

        // act
        await Index(accounts);

        // assert
        var searchResults = await Find("User", true);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[..5]), o => o.ExcludingSearchMatch());

        // assert
        searchResults = await Find("User", false);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[5..]), o => o.ExcludingSearchMatch());

        // act
        await Index([], [accounts[4], accounts[9]]);

        // assert
        searchResults = await Find("User", true);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[..4]), o => o.ExcludingSearchMatch());

        // assert
        searchResults = await Find("User", false);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[5..9]), o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindOnlyPlaceMembers()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart, 1);
        var people = await Tester.CreateUserContacts(bob, places, UniquePart);
        await Tester.SignIn(bob);

        // act
        await Index(places, people);

        // assert
        var searchResults = await Find("one", true, places.JoinedPrivatePlace1().Id);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(people.Friend1FromPrivatePlace1(), people.Friend2FromPrivatePlace1()),
                o => o.ExcludingSearchMatch());

        // assert
        searchResults = await Find("one", false, places.JoinedPrivatePlace1().Id);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(people.Stranger1FromPrivatePlace1(), people.Stranger2FromPrivatePlace1()),
                o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldTakeOnlyFromSpecifiedPlace()
    {
        // arrange
        var friendFromPlace1 = await CreateAccount("Place 1 member - Bob's friend");
        var strangerFromPlace1 = await CreateAccount("Place 1 member - Not Bob's friend");
        var friendFromPlace2 = await CreateAccount("Place 2 member - Bob's friend");
        var strangerFromPlace2 = await CreateAccount("Place 2 member - Not Bob's friend");
        var friendFromBothPlaces = await CreateAccount("Both places member - Bob's friend");
        var strangerFromBothPlaces = await CreateAccount("Both places member - Not Bob's friend");
        var bob = await Tester.SignInAsBob();
        await Tester.SignInAsUniqueAlice();
        var place1 = await Tester.CreatePlace(false, usersToInvite: bob);
        var place2 = await Tester.CreatePlace(false, usersToInvite: bob);
        await Tester.SignIn(bob);
        await Tester.CreatePeerContacts(bob, friendFromPlace1, friendFromPlace2, friendFromBothPlaces);

        // act
        var updates = ApiArray.New(
            friendFromPlace1.ToIndexedUserContact(place1.Id),
            strangerFromPlace1.ToIndexedUserContact(place1.Id),
            friendFromPlace2.ToIndexedUserContact(place2.Id),
            strangerFromPlace2.ToIndexedUserContact(place2.Id),
            friendFromBothPlaces.ToIndexedUserContact(place1.Id, place2.Id),
            strangerFromBothPlaces.ToIndexedUserContact(place1.Id, place2.Id));
        await Index(updates, []);
        await Tester.SignIn(bob);
        var searchResults = await Find("member", true, place2.Id);

        // assert
        searchResults.Should()
            .BeEquivalentTo([
                    bob.BuildSearchResult(friendFromPlace2),
                    bob.BuildSearchResult(friendFromBothPlaces),
                ],
                o => o.ExcludingSearchMatch());

        // act
        searchResults = await Find("member", false, place2.Id);

        // assert
        searchResults.Should()
            .BeEquivalentTo([
                    bob.BuildSearchResult(strangerFromPlace2),
                    bob.BuildSearchResult(strangerFromBothPlaces),
                ],
                o => o.ExcludingSearchMatch());
    }

    // Private methods

    private async Task<AccountFull[]> CreateAccounts(int count)
        => await Tester.CreateAccounts(count, secondNameFactory: i => $"{i} {UniquePart}");

    private Task<AccountFull> CreateAccount(string name)
        => Tester.CreateAccount($"{name} {UniquePart}");

    private Task Index(IReadOnlyCollection<AccountFull> updated, IReadOnlyCollection<AccountFull>? deleted = null)
    {
        return Index(BuildUserContacts(updated), BuildUserContacts(deleted ?? []));

        ApiArray<IndexedUserContact> BuildUserContacts(IReadOnlyCollection<AccountFull> accounts)
            => accounts.Select(x => x.ToIndexedUserContact()).ToApiArray();
    }

    private Task Index(IReadOnlyDictionary<TestPlaceKey, Place> places, IReadOnlyDictionary<TestUserKey, AccountFull> people)
        => Index(people.ToIndexedUserContacts(places).ToApiArray(), []);

    private async Task Index(ApiArray<IndexedUserContact> updated, ApiArray<IndexedUserContact> deleted)
    {
        await Commander.Call(new SearchBackend_UserContactBulkIndex(updated, deleted));
        await Commander.Call(new SearchBackend_Refresh(true));
    }

    private Task<ApiArray<ContactSearchResult>> Find(string criteria, bool own, PlaceId? placeId = null)
        => Tester.FindPeople($"{UniquePart} {criteria}", own, placeId);
}
