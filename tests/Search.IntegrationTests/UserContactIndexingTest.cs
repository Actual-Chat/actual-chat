using ActualChat.Search.Module;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.Users;

namespace ActualChat.Search.IntegrationTests;

[Trait("Category", "Slow")]
public class UserContactIndexingTest(ITestOutputHelper @out, ILogger<UserContactIndexingTest> log)
    : LocalAppHostTestBase( "user_contact_indexing",
        TestAppHostOptions.Default with {
            ConfigureHost = (_, cfg) => {
                cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.IsSearchEnabled)}", "true"));
                cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.ContactIndexingDelay)}", "00:00:01"));
                cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.ContactIndexingSignalInterval)}", "00:00:00.5"));
            },
        },
        @out, log)
{
    private WebClientTester _tester = null!;
    private UserContactIndexer _userContactIndexer = null!;
    private ICommander _commander = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewWebClientTester(Out);
        _userContactIndexer = AppHost.Services.GetRequiredService<UserContactIndexer>();
        _commander = AppHost.Services.Commander();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldIndexAll()
    {
        // arrange
        const int count = 30;
        var accounts = await _tester.CreateAccounts(count);

        // act
        var owner = accounts[^1];
        var ownerId = owner.Id;
        await _userContactIndexer.WhenInitialized.WaitAsync(TimeSpan.FromSeconds(10));

        // assert
        await _tester.SignInAsUniqueBob();
        var searchResults = await Find("User 2", 11);
        searchResults.Should().ContainSingle(x => x.SearchMatch.Text == "User 29");

        // act
        for (int i = 10; i < 20; i++) {
            var account = await _tester.SignIn(accounts[i].User);
            var cmd = new Accounts_Update(_tester.Session, account with { LastName = "A" + i }, account.Version);
            await _commander.Call(cmd);
        }

        // assert
        await _tester.SignIn(owner.User);
        searchResults = await Find("User A", 10);
        var expected = accounts[10..20]
            .Select(x => ownerId.BuildSearchResult(x.Id, "User A" + x.LastName, [(0, 4), (5, 8)]));
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingRank());
    }

    [Fact]
    public async Task ShouldReindexPlaces()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var accounts = await _tester.CreateAccounts(5);
        var place1 = await _tester.CreatePlace(false, "Place 1 one", accounts);

        // act
        await _userContactIndexer.WhenInitialized.WaitAsync(TimeSpan.FromSeconds(10));

        // assert
        await _tester.SignIn(bob);
        var searchResults = await Find("User", accounts.Length);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(accounts), o => o.ExcludingSearchMatch());

        searchResults = await Find("User", accounts.Length, place1.Id);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(accounts), o => o.ExcludingSearchMatch());

        // act
        var place2 = await _tester.CreatePlace(false, "Place 2 two", accounts[..3]);

        // assert
        searchResults = await Find("User", 3, place2.Id);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(accounts[..3]), o => o.ExcludingSearchMatch());
    }

    private async Task<ApiArray<ContactSearchResult>> Find(string criteria, int expectedCount, PlaceId? placeId = null)
    {
        ApiArray<ContactSearchResult> searchResults = [];
        await TestExt.When(async () => {
                searchResults = await _tester.FindPeople(criteria, false, placeId, 20);
                Log.LogInformation("Found {FoundCount} out of expected {ExpectedCount}",
                        searchResults.Count,
                        expectedCount);
                searchResults.Should().HaveCount(expectedCount);
            },
            TimeSpan.FromSeconds(10));
        return searchResults;
    }
}
