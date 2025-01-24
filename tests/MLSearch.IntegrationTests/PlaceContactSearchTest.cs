using ActualChat.Search;
using ActualChat.Testing.Host;
using static ActualChat.Testing.Host.Assertion.AssertOptionsExt;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
public class PlaceContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private BlazorTester Tester { get; } = fixture.AppHost.NewBlazorTester(@out);
    private string UniquePart { get; } = UniqueNames.Prefix();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldFindPlaces()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(places.Joined());
        var searchResults = await Find("pla", true, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        expected = bob.BuildSearchResults(places.OtherPublic());
        searchResults = await Find("pla", false, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        expected = bob.BuildSearchResults(places.Joined1());
        searchResults = await Find("one", true, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        expected = bob.BuildSearchResults(places.OtherPublicPlace1());
        searchResults = await Find("one", false, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        expected = [
            bob.BuildSearchResult(places.JoinedPublicPlace1(), UniquePart, [new (7, 12), new (16, 19)]),
            bob.BuildSearchResult(places.JoinedPrivatePlace1(), UniquePart, [new (8, 13), new (17, 20)]),
        ];
        searchResults = await Find("place one", true, expected.Count);
        searchResults.Should()
            .BeEquivalentTo(expected, o => o.ExcludingRank());

        expected = [bob.BuildSearchResult(places.OtherPublicPlace1(), UniquePart, [new (7, 12), new (16, 19)])];
        searchResults = await Find("place one", false, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingRank());
    }

    [Fact]
    public async Task ShouldFindUpdatedPlaces()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var alice = await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(places.Joined());
        var searchResults = await Find("pla", true, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        expected = bob.BuildSearchResults(places.OtherPublic());
        searchResults = await Find("pla", false, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act
        await Tester.SignIn(alice);
        var updatedPlace = await Tester.UpdatePlace(places.JoinedPrivatePlace1().Id, $"{UniquePart} bbb");
        await Tester.SignIn(bob);

        // assert
        expected = bob.BuildSearchResults(places.Joined().Except([places.JoinedPrivatePlace1()]));
        searchResults = await Find("pla", true, expected.Count);
        searchResults.Should()
            .BeEquivalentTo(
                expected,
                o => o.ExcludingSearchMatch());

        // assert
        expected = bob.BuildSearchResults(updatedPlace);
        searchResults = await Find("bbb", true, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindDeletedPlaces()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var alice = await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(places.Joined());
        var searchResults = await Find("pla", true, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        expected = bob.BuildSearchResults(places.OtherPublic());
        searchResults = await Find("pla", false, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act
        await Tester.SignIn(alice);
        await Tester.DeletePlace(places.JoinedPrivatePlace1().Id);
        await Tester.SignIn(bob);

        // assert
        expected = bob.BuildSearchResults(places.Joined().Except([places.JoinedPrivatePlace1()]));
        searchResults = await Find("pla", true, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    private Task<ApiArray<ContactSearchResult>> Find(string criteria, bool own, int expected)
        => TestsExt.When(async () => {
                var results = await Tester.FindPlaces($"{UniquePart} {criteria}", own);
                results.Should().HaveCount(expected);
                return results;
            },
            Intervals.Fixed(TimeSpan.FromSeconds(0.5)),
            TimeSpan.FromSeconds(30));
}
