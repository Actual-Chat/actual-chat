using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Search;
using ActualChat.Testing.Host;
using static ActualChat.Testing.Host.Assertion.AssertOptionsExt;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
public class PlaceContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private BlazorTester Tester { get; } = fixture.AppHost.NewBlazorTester(@out);
    private IContactsBackend ContactsBackend { get; } = fixture.AppHost.Services.GetRequiredService<IContactsBackend>();
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

        // act
        await Index(places);
        await Tester.SignIn(bob.User);
        await WaitUntilIndexed(places.Joined().Select(x => x.Id).ToList());

        // assert
        var searchResults = await Find("pla", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.Joined().ToArray()), o => o.ExcludingSearchMatch());

        searchResults = await Find("pla", false);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.OtherPublic().ToArray()), o => o.ExcludingSearchMatch());

        searchResults = await Find("one", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.Joined1().ToArray()), o => o.ExcludingSearchMatch());

        searchResults = await Find("one", false);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.OtherPublicPlace1()), o => o.ExcludingSearchMatch());

        searchResults = await Find("place one", true);
        searchResults.Should()
            .BeEquivalentTo([
                    bob.BuildSearchResult(places.JoinedPublicPlace1(), [new (7, 12), new (16, 19), (20, 25)]),
                    bob.BuildSearchResult(places.JoinedPrivatePlace1(), [new (8, 13), new (17, 20), (21, 26)]),
                ],
                o => o.ExcludingRank());

        searchResults = await Find("place one", false);
        searchResults.Should()
            .BeEquivalentTo(
                [bob.BuildSearchResult(places.OtherPublicPlace1(), [new (7, 12), new (16, 19), (20, 25)])],
                o => o.ExcludingRank());
    }

    [Fact]
    public async Task ShouldFindUpdatedPlaces()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);

        // act
        await Index(places);
        await Tester.SignIn(bob.User);
        await WaitUntilIndexed(places.Joined().Select(x => x.Id).ToList());

        // assert
        var searchResults = await Find("pla", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.Joined().ToArray()), o => o.ExcludingSearchMatch());

        searchResults = await Find("pla", false);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.OtherPublic().ToArray()), o => o.ExcludingSearchMatch());

        // act
        var updatedPlace = places.JoinedPrivatePlace1() with { Title = $"{UniquePart} bbb" };
        await Index([updatedPlace]);

        // assert
        searchResults = await Find("pla", true);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(places.Joined()
                    .Except([places.JoinedPrivatePlace1()])
                    .ToArray()),
                o => o.ExcludingSearchMatch());

        // assert
        searchResults = await Find("bbb", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(updatedPlace), o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindDeletedPlaces()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);

        // act
        await Index(places);
        await Tester.SignIn(bob.User);
        await WaitUntilIndexed(places.Joined().Select(x => x.Id).ToList());

        // assert
        var searchResults = await Find("pla", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.Joined().ToArray()), o => o.ExcludingSearchMatch());

        searchResults = await Find("pla", false);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(places.OtherPublic().ToArray()), o => o.ExcludingSearchMatch());

        // act
        await Index([], [places.JoinedPrivatePlace1()]);

        // assert
        searchResults = await Find("pla", true);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(places.Joined()
                    .Except([places.JoinedPrivatePlace1()])
                    .ToArray()),
                o => o.ExcludingSearchMatch());
    }

    private Task Index(IReadOnlyDictionary<TestPlaceKey, Place> places)
        => Index(places.Values);

    private async Task Index(IEnumerable<Place> updated, IEnumerable<Place>? deleted = null)
    {
        var updatedContacts = updated.Select(x => x.ToIndexedPlaceContact()).ToApiArray();
        var deletedContacts = (deleted ?? []).Select(x => x.ToIndexedPlaceContact()).ToApiArray();
        await Commander.Call(new SearchBackend_PlaceContactBulkIndex(updatedContacts, deletedContacts));
        await Commander.Call(new SearchBackend_Refresh(refreshPlaces: true));
    }

    private Task<ApiArray<ContactSearchResult>> Find(string criteria, bool own)
        => Tester.FindPlaces($"{UniquePart} {criteria}", own);

    private async Task WaitUntilIndexed(IReadOnlyCollection<PlaceId> expectedIds, CancellationToken cancellationToken = default)
    {
        var owner = await Tester.GetOwnAccount(cancellationToken);
        await TestExt.When(async () => {
            var placeIds = await ContactsBackend.ListPlaceIds(owner.Id, cancellationToken);
            placeIds.Should().BeEquivalentTo(expectedIds);
        }, TimeSpan.FromSeconds(10));
    }
}
