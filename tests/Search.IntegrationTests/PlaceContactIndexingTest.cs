using ActualChat.Search.Module;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;

namespace ActualChat.Search.IntegrationTests;

[Trait("Category", "Slow")]
public class PlaceContactIndexingTest(ITestOutputHelper @out)
    : LocalAppHostTestBase( "place_contact_indexing",
        TestAppHostOptions.Default with {
            ConfigureHost = (_, cfg) => {
                cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.IsSearchEnabled)}", "true"));
                cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.ContactIndexingDelay)}", "00:00:01"));
                cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.ContactIndexingSignalInterval)}", "00:00:00.5"));
            },
        },
        @out)
{
    private WebClientTester _tester = null!;
    private PlaceContactIndexer _placeContactIndexer = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewWebClientTester(Out);
        _placeContactIndexer = AppHost.Services.GetRequiredService<PlaceContactIndexer>();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldIndexAll()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob);

        // act
        await _tester.SignIn(bob);

        // assert
        await _placeContactIndexer.WhenInitialized.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = places.Joined().ToArray();
        var searchResults = await Find( true, "place", expected.Length);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(expected),
                o => o.ExcludingSearchMatch()
            );

        expected = places.OtherPublic().ToArray();
        searchResults = await Find( false, "place", expected.Length);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(expected),
                o => o.ExcludingSearchMatch()
            );
    }

    private async Task<ApiArray<ContactSearchResult>> Find(bool own, string criteria, int expectedCount, int requestCount = 20)
    {
        ApiArray<ContactSearchResult> searchResults = [];
        await TestExt.When(async () => {
                searchResults = await _tester.FindPlaces(criteria, own, limit: requestCount);
                Out.WriteLine("Found {0} out of expected {1}",
                    searchResults.Count,
                    expectedCount);
                searchResults.Should().HaveCount(expectedCount);
            },
            Intervals.Exponential(TimeSpan.FromMilliseconds(100), 1.5, TimeSpan.FromMilliseconds(500)), TimeSpan.FromSeconds(10));
        return searchResults;
    }
}
