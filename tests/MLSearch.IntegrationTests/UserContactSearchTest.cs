using System.Diagnostics.CodeAnalysis;
using ActualChat.Contacts;
using ActualChat.Search;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.Users;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
public class UserContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private BlazorTester Tester { get; } = fixture.AppHost.NewBlazorTester(@out);
    private string UniquePart { get; } = UniqueNames.Prefix();
    [field: AllowNull, MaybeNull]
    private string DeviceId => field ??= $"Device-{UniquePart}";

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
        await CreateAccounts(10);
        await Tester.SignInAsUniqueBob();

        // act
        await Find("User", false, null, 10); // wait until indexed
        var searchResults = await Find("User", true, null, 0);

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
        await Find("User", true, null, 10); // wait until indexed
        var searchResults = await Find("User", false, null, 0);

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

        // act, assert
        var expected = bob.BuildSearchResults(accounts[..5]);
        var searchResults = await Find("User", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(accounts[5..]);
        searchResults = await Find("User", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindUsersLinkedByPhone()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var accounts = await CreateAccounts(10);
        await Tester.SaveExternalContacts(accounts.Take(5).Select(x => NewExternalContact(bob).WithDisplayName($"Friend {x.Name}").WithPhone(x.Phone)));

        // act, assert
        var expected = bob.BuildSearchResults(accounts[..5].Select(x => x with { Name = $"Friend {x.Name}" }));
        var searchByContactNameResults = await Find("Friend", true, null, expected.Count);
        searchByContactNameResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        var searchResults = await Find("User", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(searchByContactNameResults, o => o.ExcludingSearchMatch());

        // act, assert
        searchResults = await Find("Friend", false, null, 0);
        searchResults.Should().BeEmpty();

        // act, assert
        expected = bob.BuildSearchResults(accounts[5..]);
        searchResults = await Find("User", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindUsersByExternalContactName()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var accounts = await CreateAccounts(10);
        await Tester.SaveExternalContacts(accounts.Take(5).Select(x => NewExternalContact(bob).WithDisplayName($"Friend - External Contact {x.Name}").WithPhone(x.Phone)));

        // act, assert
        var expected = bob.BuildSearchResults(accounts[..5].Select(x => x with { Name = $"Friend - External Contact {x.Name}" }));
        var searchByExternalContactNameResults = await Find("Ext", true, null, expected.Count);
        searchByExternalContactNameResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        var searchResults = await Find("User", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(searchByExternalContactNameResults, o => o.ExcludingSearchMatch());

        // act, assert
        searchResults = await Find("Friend", false, null, 0);
        searchResults.Should().BeEmpty();

        // act, assert
        expected = bob.BuildSearchResults(accounts[5..]);
        searchResults = await Find("User", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact(Skip = "Flaky")]
    public async Task ShouldFindByPrefix()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var people = await Tester.CreateUserContacts(bob, places, UniquePart);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(people.Friends());
        var searchResults = await Find("us", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(people.Strangers());
        searchResults = await Find("us", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(people.Friends());
        searchResults = await Find("from u", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(people.Strangers());
        searchResults = await Find("from u", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
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
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(people.Friend1FromPublicPlace1(), people.Friend2FromPublicPlace1());
        var searchResults = await Find("us", true, places.JoinedPublicPlace1().Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(people.Stranger1FromPublicPlace1(), people.Stranger2FromPublicPlace1());
        searchResults = await Find("us", false, places.JoinedPublicPlace1().Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(people.Friend1FromPublicPlace1(), people.Friend2FromPublicPlace1());
        searchResults = await Find("from u", true, places.JoinedPublicPlace1().Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(people.Stranger1FromPublicPlace1(), people.Stranger2FromPublicPlace1());
        searchResults = await Find("from u", false, places.JoinedPublicPlace1().Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindByMultipleWords()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var people = await Tester.CreateUserContacts(bob, places, UniquePart);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(
            people.Friend1FromPublicPlace2(),
                people.Friend1FromPrivatePlace2(),
                people.Friend2FromPublicPlace1(),
                people.Friend2FromPublicPlace2(),
                people.Friend2FromPrivatePlace1(),
                people.Friend2FromPrivatePlace2());
        var searchResults = await Find("user tw", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(
            people.Stranger1FromPublicPlace2(),
                people.Stranger1FromPrivatePlace2(),
                people.Stranger2FromPublicPlace1(),
                people.Stranger2FromPublicPlace2(),
                people.Stranger2FromPrivatePlace1(),
                people.Stranger2FromPrivatePlace2());
        searchResults = await Find("user tw", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
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

        // act, assert
        var expected = bob.BuildSearchResults(accounts[..5]);
        var searchResults = await Find("User", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(accounts[5..]);
        searchResults = await Find("User", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act
        await Tester.UpdateAccount(accounts[4] with { Name = "aaa" });
        await Tester.UpdateAccount(accounts[9] with { Name = "aaa" });

        // act, assert
        expected = bob.BuildSearchResults(accounts[..4]);
        searchResults = await Find("User", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(accounts[5..9]);
        searchResults = await Find("User", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
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

        // act, assert
        var expected = bob.BuildSearchResults(accounts[..5]);
        var searchResults = await Find("User", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        searchResults = await Find("User", false, null, expected.Count);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(accounts[5..]), o => o.ExcludingSearchMatch());

        // act
        await Tester.DeleteAccount(accounts[4]);
        await Tester.DeleteAccount(accounts[9]);
        // Note: intentional manual refresh only for tests to avoid waiting for scheduled refresh
        // For removals we do not force manual refresh
        await Tester.Commander.Call(new SearchBackend_Refresh(RefreshUsers: true));

        // act, assert
        expected = bob.BuildSearchResults(accounts[..4]);
        searchResults = await Find("User", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // assert
        expected = bob.BuildSearchResults(accounts[5..9]);
        searchResults = await Find("User", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
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

        // act, assert
        var expected = bob.BuildSearchResults(people.Friend1FromPrivatePlace1(), people.Friend2FromPrivatePlace1());
        var searchResults = await Find("one", true, places.JoinedPrivatePlace1().Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // assert
        expected = bob.BuildSearchResults(people.Stranger1FromPrivatePlace1(), people.Stranger2FromPrivatePlace1());
        searchResults = await Find("one", false, places.JoinedPrivatePlace1().Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
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
        await Tester.CreatePlace(false,
            nameof(ShouldTakeOnlyFromSpecifiedPlace),
            bob,
            friendFromPlace1,
            friendFromBothPlaces,
            strangerFromPlace1,
            strangerFromBothPlaces);
        var place2 = await Tester.CreatePlace(false,
            nameof(ShouldTakeOnlyFromSpecifiedPlace),
            bob,
            friendFromPlace2,
            friendFromBothPlaces,
            strangerFromPlace2,
            strangerFromBothPlaces);
        await Tester.SignIn(bob);
        await Tester.CreatePeerContacts(bob, friendFromPlace1, friendFromPlace2, friendFromBothPlaces);

        // act, assert
        var expected = bob.BuildSearchResults(friendFromPlace2, friendFromBothPlaces);
        var searchResults = await Find("member", true, place2.Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(strangerFromPlace2, strangerFromBothPlaces);
        searchResults = await Find("member", false, place2.Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    // Private methods

    private ExternalContactFull NewExternalContact(AccountFull owner)
        => new (ExternalContactId.New(UserDeviceId.New(owner.Id, DeviceId), NewDeviceContactId()));

    private static Symbol NewDeviceContactId()
        => UniqueNames.Prefix();

    private async Task<AccountFull[]> CreateAccounts(int count)
        => await Tester.CreateAccounts(count, secondNameFactory: i => $"{i} {UniquePart}");

    private Task<AccountFull> CreateAccount(string name)
        => Tester.CreateAccount($"{name} {UniquePart}");

    private Task<ContactSearchResult[]> Find(
        string criteria,
        bool own,
        PlaceId? placeId = null,
        int expectedCount = 1)
        => TestsExt.When(async () => {
                var people = await Tester.FindPeople($"{UniquePart} {criteria}", own, placeId);
                people.Should().HaveCount(expectedCount);
                return people;
            },
            Intervals.Fixed(TimeSpan.FromSeconds(0.5)),
            TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(20));
}
