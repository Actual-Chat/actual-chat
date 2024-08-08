using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualLab.Generators;
using ActualLab.Mathematics;
using static ActualChat.Testing.Host.Assertion.AssertOptionsExt;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection))]
[Trait("Category", "Slow")]
public class PlaceContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
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
    public async Task ShouldFindPlaces()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob);

        // act
        var updates = places.Values.Select(x => x.ToIndexedPlaceContact()).ToApiArray();
        await _commander.Call(new SearchBackend_PlaceContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(refreshPlaces: true));

        // assert
        await _tester.SignIn(bob.User);
        var searchResults = await _tester.FindPlaces("pla", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.Joined().ToArray()), o => o.ExcludingSearchMatch());

        searchResults = await _tester.FindPlaces("pla", false);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.OtherPublic().ToArray()), o => o.ExcludingSearchMatch());

        searchResults = await _tester.FindPlaces("one", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.Joined1().ToArray()), o => o.ExcludingSearchMatch());

        searchResults = await _tester.FindPlaces("one", false);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.OtherPublicPlace1()), o => o.ExcludingSearchMatch());

        searchResults = await _tester.FindPlaces("place one", true);
        searchResults.Should()
            .BeEquivalentTo([
                    bob.BuildSearchResult(places.JoinedPublicPlace1(), [new (7, 12), new (16, 19)]),
                    bob.BuildSearchResult(places.JoinedPrivatePlace1(), [new (8, 13), new (17, 20)]),
                ],
                o => o.ExcludingRank());

        searchResults = await _tester.FindPlaces("place one", false);
        searchResults.Should()
            .BeEquivalentTo(
                [bob.BuildSearchResult(places.OtherPublicPlace1(), [new (7, 12), new (16, 19)])],
                o => o.ExcludingRank());
    }

    [Fact]
    public async Task ShouldFindUpdatedPlaces()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob);

        // act
        var updates = places.Values.Select(x => x.ToIndexedPlaceContact()).ToApiArray();
        await _commander.Call(new SearchBackend_PlaceContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(refreshPlaces: true));

        // assert
        await _tester.SignIn(bob.User);
        var searchResults = await _tester.FindPlaces("pla", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.Joined().ToArray()), o => o.ExcludingSearchMatch());

        searchResults = await _tester.FindPlaces("pla", false);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.OtherPublic().ToArray()), o => o.ExcludingSearchMatch());

        // act
        var updatedPlace = places.JoinedPrivatePlace1() with { Title = "bbb" };
        await _commander.Call(new SearchBackend_PlaceContactBulkIndex([updatedPlace.ToIndexedPlaceContact()], []));
        await _commander.Call(new SearchBackend_Refresh(refreshPlaces: true));

        // assert
        searchResults = await _tester.FindPlaces("pla", true);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(places.Joined()
                    .Except([places.JoinedPrivatePlace1()])
                    .ToArray()),
                o => o.ExcludingSearchMatch());

        // assert
        searchResults = await _tester.FindPlaces("bbb", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(updatedPlace), o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindDeletedPlaces()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob);

        // act
        var updates = places.Values.Select(x => x.ToIndexedPlaceContact()).ToApiArray();
        await _commander.Call(new SearchBackend_PlaceContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(refreshPlaces: true));

        // assert
        await _tester.SignIn(bob.User);
        var searchResults = await _tester.FindPlaces("pla", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.Joined().ToArray()), o => o.ExcludingSearchMatch());

        searchResults = await _tester.FindPlaces("pla", false);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.OtherPublic().ToArray()), o => o.ExcludingSearchMatch());

        // act
        var deletedPlace = places.JoinedPrivatePlace1();
        await _commander.Call(new SearchBackend_PlaceContactBulkIndex([], [deletedPlace.ToIndexedPlaceContact()]));
        await _commander.Call(new SearchBackend_Refresh(refreshPlaces: true));

        // assert
        searchResults = await _tester.FindPlaces("pla", true);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(places.Joined()
                    .Except([places.JoinedPrivatePlace1()])
                    .ToArray()),
                o => o.ExcludingSearchMatch());
    }
}
