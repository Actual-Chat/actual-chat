using ActualChat.Search;
using ActualChat.Testing.Host;
using static ActualChat.Testing.Host.Assertion.AssertOptionsExt;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
public class PlaceContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);
    private string IsolationKey { get; } = UniqueNames.Random();

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
        var places = await Tester.CreatePlaceContacts(bob, IsolationKey);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(places.Joined());
        var searchResults = await Find("pla", true, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        expected = bob.BuildSearchResults(places.OtherPublic());
        searchResults = await Find("pla", false, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        expected = bob.BuildSearchResults(places.Joined1());
        searchResults = await Find(TestSearchDataGenerator.OneTerm, true, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        expected = bob.BuildSearchResults(places.OtherPublicPlace1());
        searchResults = await Find(TestSearchDataGenerator.OneTerm, false, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        expected = [
            bob.BuildSearchResult(places.JoinedPublicPlace1(), IsolationKey, [new (7, 12), new (15, 20)]),
            bob.BuildSearchResult(places.JoinedPrivatePlace1(), IsolationKey, [new (8, 13), new (16, 21)]),
        ];
        searchResults = await Find($"place {TestSearchDataGenerator.OneTerm}", true, expected.Count);
        searchResults.Should()
            .BeEquivalentTo(expected, o => o.ExcludingRank());

        expected = [bob.BuildSearchResult(places.OtherPublicPlace1(), IsolationKey, [new (7, 12), new (15, 20)])];
        searchResults = await Find($"place {TestSearchDataGenerator.OneTerm}", false, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingRank());
    }

    [Fact]
    public async Task ShouldFindUpdatedPlaces()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var alice = await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, IsolationKey);
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
        var updatedPlace = await Tester.UpdatePlace(places.JoinedPrivatePlace1().Id, $"{IsolationKey} bbb");
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
        var places = await Tester.CreatePlaceContacts(bob, IsolationKey);
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

    private Task<FoundContact[]> Find(string criteria, bool own, int expected)
        => TestsExt.When(async () => {
                var results = await Tester.FindPlaces($"{IsolationKey} {criteria}", own);
                results.Should().HaveCount(expected);
                return results;
            },
            Intervals.Fixed(TimeSpan.FromSeconds(0.5)),
            TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(20));
}
