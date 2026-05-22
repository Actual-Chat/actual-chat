using ActualChat.Contacts;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.Search;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using OpenSearch.Client;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
public class UserContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);
    private IOpenSearchClient OpenSearchClient => field ??= AppHost.Services.GetRequiredService<IOpenSearchClient>();
    private OpenSearchNames OpenSearchNames => field ??= AppHost.Services.GetRequiredService<OpenSearchNames>();
    private string IsolationKey { get; } = UniqueNames.Random();
    private string DeviceId => field ??= $"Device-{IsolationKey}";

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
        await Find("TestUser", false, null, 10); // wait until indexed
        var searchResults = await Find("TestUser", true, null, 0);

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
        await Find("TestUser", true, null, 10); // wait until indexed
        var searchResults = await Find("TestUser", false, null, 0);

        // assert
        searchResults.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldNotFindSelf(bool own)
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var accounts = await CreateAccounts(3);
        var ownAccount = await Tester.SignIn(accounts[0]);
        if (own)
            foreach (var other in accounts[1..])
                await Tester.CreatePeerContact(ownAccount, other);

        // act
        var searchResults = await Find("TestUser", own, null, 2);

        // assert
        var expected = ownAccount.BuildSearchResults(accounts[1..]);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindUsersThatAreOtherUsersFriends()
    {
        var alice = await Tester.SignInAsUniqueAlice();
        var accounts = await CreateAccounts(3);
        foreach (var other in accounts)
            await Tester.CreatePeerContact(alice, other);

        // wait until Alice's contacts are indexed
        await Find("TestUser", true, null, accounts.Length);

        // act: Bob is unrelated to Alice and these accounts — non-contact search must still find them
        var bob = await Tester.SignInAsUniqueBob();
        var expected = bob.BuildSearchResults(accounts);
        var searchResults = await Find("TestUser", false, null, expected.Count);

        // assert
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
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
        var searchResults = await Find("TestUser", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(accounts[5..]);
        searchResults = await Find("TestUser", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindUsersLinkedByPhone()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var accounts = await CreateAccounts(10);
        // Display name format: "{x.Name} Friend" ensures unique part comes first for phrase prefix search
        await Tester.SaveExternalContacts(accounts.Take(5).Select(x => NewExternalContact(bob).WithDisplayName($"{x.Name} Friend").WithPhone(x.Phone)));

        // act, assert
        var expected = bob.BuildSearchResults(accounts[..5].Select(x => x with { Name = $"{x.Name} Friend" }));
        var searchByContactNameResults = await Find("Friend", true, null, expected.Count);
        searchByContactNameResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        var searchResults = await Find("TestUser", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(searchByContactNameResults, o => o.ExcludingSearchMatch());

        // act, assert
        searchResults = await Find("Friend", false, null, 0);
        searchResults.Should().BeEmpty();

        // act, assert
        expected = bob.BuildSearchResults(accounts[5..]);
        searchResults = await Find("TestUser", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindUsersByExternalContactName()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var accounts = await CreateAccounts(10);
        // Display name format: "{x.Name} Friend External Contact" ensures unique part comes first for phrase prefix search
        await Tester.SaveExternalContacts(accounts.Take(5).Select(x => NewExternalContact(bob).WithDisplayName($"{x.Name} Friend External Contact").WithPhone(x.Phone)));

        // act, assert
        var expected = bob.BuildSearchResults(accounts[..5].Select(x => x with { Name = $"{x.Name} Friend External Contact" }));
        var searchByExternalContactNameResults = await Find("Ext", true, null, expected.Count);
        searchByExternalContactNameResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        var searchResults = await Find("TestUser", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(searchByExternalContactNameResults, o => o.ExcludingSearchMatch());

        // act, assert
        searchResults = await Find("Friend", false, null, 0);
        searchResults.Should().BeEmpty();

        // act, assert
        expected = bob.BuildSearchResults(accounts[5..]);
        searchResults = await Find("TestUser", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindByPrefix()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, IsolationKey);
        var people = await Tester.CreateUserContacts(bob, places, IsolationKey);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(people.Friends());
        var searchResults = await Find("testu", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(people.Strangers());
        searchResults = await Find("testu", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(people.Friends());
        searchResults = await Find("from te", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(people.Strangers());
        searchResults = await Find("from te", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindByPrefixInPlace()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, IsolationKey);
        var people = await Tester.CreateUserContacts(bob, places, IsolationKey);

        // act
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(people.Friend1FromPublicPlace1(), people.Friend2FromPublicPlace1());
        var searchResults = await Find("from te", true, places.JoinedPublicPlace1().Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(people.Stranger1FromPublicPlace1(), people.Stranger2FromPublicPlace1());
        searchResults = await Find("from te", false, places.JoinedPublicPlace1().Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindByMultipleWords()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, IsolationKey);
        var people = await Tester.CreateUserContacts(bob, places, IsolationKey);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(
            people.Friend1FromPublicPlace2(),
                people.Friend1FromPrivatePlace2(),
                people.Friend2FromPublicPlace1(),
                people.Friend2FromPublicPlace2(),
                people.Friend2FromPrivatePlace1(),
                people.Friend2FromPrivatePlace2());
        var searchResults = await Find("testuser T_W", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(
            people.Stranger1FromPublicPlace2(),
                people.Stranger1FromPrivatePlace2(),
                people.Stranger2FromPublicPlace1(),
                people.Stranger2FromPublicPlace2(),
                people.Stranger2FromPrivatePlace1(),
                people.Stranger2FromPrivatePlace2());
        searchResults = await Find("testuser T_W", false, null, expected.Count);
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
        var searchResults = await Find("TestUser", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(accounts[5..]);
        searchResults = await Find("TestUser", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act
        await Tester.UpdateAccount(accounts[4] with { Name = "aaa" });
        await Tester.UpdateAccount(accounts[9] with { Name = "aaa" });

        // act, assert
        expected = bob.BuildSearchResults(accounts[..4]);
        searchResults = await Find("TestUser", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        expected = bob.BuildSearchResults(accounts[5..9]);
        searchResults = await Find("TestUser", false, null, expected.Count);
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
        var searchResults = await Find("TestUser", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act, assert
        searchResults = await Find("TestUser", false, null, expected.Count);
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
        searchResults = await Find("TestUser", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // assert
        expected = bob.BuildSearchResults(accounts[5..9]);
        searchResults = await Find("TestUser", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindOnlyPlaceMembers()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, IsolationKey, 1);
        var people = await Tester.CreateUserContacts(bob, places, IsolationKey);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(people.Friend1FromPrivatePlace1(), people.Friend2FromPrivatePlace1());
        var searchResults = await Find(TestSearchDataGenerator.OneTerm, true, places.JoinedPrivatePlace1().Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // assert
        expected = bob.BuildSearchResults(people.Stranger1FromPrivatePlace1(), people.Stranger2FromPrivatePlace1());
        searchResults = await Find(TestSearchDataGenerator.OneTerm, false, places.JoinedPrivatePlace1().Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldTakeOnlyFromSpecifiedPlace()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice(); // Initialize session before creating accounts
        var friendFromPlace1 = await CreateAccount("Place 1 member - Bob's friend");
        var strangerFromPlace1 = await CreateAccount("Place 1 member - Not Bob's friend");
        var friendFromPlace2 = await CreateAccount("Place 2 member - Bob's friend");
        var strangerFromPlace2 = await CreateAccount("Place 2 member - Not Bob's friend");
        var friendFromBothPlaces = await CreateAccount("Both places member - Bob's friend");
        var strangerFromBothPlaces = await CreateAccount("Both places member - Not Bob's friend");
        var bob = await Tester.SignInAsUniqueBob();

        await Tester.SignIn(alice);
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

    [Fact]
    public async Task ShouldNotIndexSystemUsers()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var sentinel = await CreateAccount("Sentinel");
        await Tester.SignIn(bob);

        // act
        foreach (var systemUserId in Constants.User.SystemUserIds)
            await Tester.CreatePeerContact(bob, new Account(systemUserId));
        await Tester.CreatePeerContact(bob, sentinel);

        // Wait until the sentinel contact is indexed — that proves UserContactIndexingFlow
        // processed every peer contact created before it, including the system-user ones.
        await TestsExt.When(async () => {
                var response = await GetIndexedUser(sentinel.Id);
                response.Found.Should().BeTrue();
                return response;
            },
            TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(20));

        // assert
        foreach (var systemUserId in Constants.User.SystemUserIds) {
            var response = await GetIndexedUser(systemUserId);
            response.Found.Should()
                .BeFalse("system user {0} must not be indexed in OpenSearch", systemUserId);
        }
    }

    // Private methods

    private ExternalContactFull NewExternalContact(AccountFull owner)
        => new (ExternalContactId.New(UserDeviceId.New(owner.Id, DeviceId), NewDeviceContactId()));

    private static Symbol NewDeviceContactId()
        => UniqueNames.Random();

    private async Task<AccountFull[]> CreateAccounts(int count)
        => await Tester.CreateAccounts(count,
            userNameFactory: i => $"{IsolationKey} TestUser {i}",
            nameFactory: i => $"{IsolationKey} TestUser",
            secondNameFactory: i => $"{i}");

    private Task<AccountFull> CreateAccount(string name)
        => Tester.CreateAccount($"{IsolationKey} {name}");

    private Task<GetResponse<IndexedUser>> GetIndexedUser(UserId userId)
        => OpenSearchClient.GetAsync<IndexedUser>(userId.Value,
            s => s.Index(OpenSearchNames.UserIndexName).Routing(userId.Value));

    private Task<FoundContact[]> Find(
        string criteria,
        bool own,
        PlaceId? placeId = null,
        int expectedCount = 1)
        => TestsExt.When(async () => {
                var people = await Tester.FindPeople($"{IsolationKey} {criteria}", own, placeId);
                people.Should().HaveCount(expectedCount);
                return people;
            },
            Intervals.Fixed(TimeSpan.FromSeconds(0.5)),
            TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(20));
}
