using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using SearchUI = ActualChat.UI.Blazor.App.Services.SearchUI;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(SearchUICollection))]
public class SearchUITest(SearchAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<SearchAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);
    private string IsolationKey { get; } = UniqueNames.Random();
    private SearchUI SearchUI => Tester.ScopedAppServices.AppUIHub().SearchUI;

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact(Skip = "Flaky")]
    public async Task ShouldFindOnCriteriaChange()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, IsolationKey);
        var groups = await Tester.CreateGroupContacts(bob, places, testIsolationKey: IsolationKey);
        var people = await Tester.CreateUserContacts(bob, places, IsolationKey);
        var entries = await Tester.CreateEntries(bob, groups, people, IsolationKey);

        // act
        await Tester.SignIn(bob);
        var expectedFriends = people.Friends("one");
        var expectedStrangers = people.Strangers("one");
        var expectedJoinedGroups = groups.Joined("one");
        var expectedOtherGroups = groups.OtherPublic("one");
        var expectedJoinedPlaces = places.Joined("one");
        var expectedOtherPlaces = places.OtherPublic("one");
        var expectedEntries = entries.Accessible("one");
        await WaitUntilIndexed("one",
            null,
            expectedFriends,
            expectedStrangers,
            expectedJoinedGroups,
            expectedOtherGroups,
            expectedEntries,
            expectedJoinedPlaces,
            expectedOtherPlaces);
        SearchUI.Text.Value = $"{IsolationKey} one";

        // assert
        var foundItems = await GetSearchResults(GetExpectedCount());
        AssertFoundItems(0, bob.BuildFoundContacts(false, expectedFriends));
        AssertFoundItems(3, bob.BuildFoundContacts(false, expectedJoinedGroups));
        AssertFoundItems(6, bob.BuildFoundContacts(false, expectedJoinedPlaces));
        AssertFoundItems(8, expectedEntries.BuildFoundEntries(["one"], IsolationKey));
        AssertFoundItems(11,
            bob.BuildFoundContacts(true, expectedStrangers),
            bob.BuildFoundContacts(true, expectedOtherGroups),
            bob.BuildFoundContacts(true, expectedOtherPlaces));

        // arrange
        expectedFriends = [people.Friend1FromPrivatePlace2()];
        expectedStrangers = [people.Stranger1FromPrivatePlace2()];
        expectedJoinedGroups = [
            groups.JoinedPrivatePlace2JoinedPrivateChat1(),
            groups.JoinedPrivatePlace2JoinedPublicChat1(),
        ];
        expectedOtherGroups = [];
        expectedJoinedPlaces = [];
        expectedOtherPlaces = [];
        expectedEntries = entries.AccessibleFromJoinedPrivatePlace2("one");

        // act
        await WaitUntilIndexed("one",
            places.JoinedPrivatePlace2().Id,
            expectedFriends,
            expectedStrangers,
            expectedJoinedGroups,
            expectedOtherGroups,
            expectedEntries);
        SearchUI.PlaceId.Value = places.JoinedPrivatePlace2().Id;
        SearchUI.Text.Value = $"{IsolationKey} one";

        // assert
        foundItems = await GetSearchResults(GetExpectedCount());
        AssertFoundItems(0, bob.BuildFoundContacts(false, expectedFriends));
        AssertFoundItems(1, bob.BuildFoundContacts(false, expectedJoinedGroups));
        AssertFoundItems(3, expectedEntries.BuildFoundEntries(["one"], IsolationKey));
        AssertFoundItems(6,
            bob.BuildFoundContacts(true, expectedStrangers),
            bob.BuildFoundContacts(true, expectedOtherGroups));
        return;

        void AssertFoundItems(int iStart, params IReadOnlyCollection<IReadOnlyCollection<FoundItem>> expectedFoundItemLists)
        {
            var expected = expectedFoundItemLists.SelectMany(x => x).ToList();
            var expectedCount = expectedFoundItemLists.Sum(x => x.Count.Clamp(0, Constants.Search.DefaultPageSize));
            for (int i = iStart; i < iStart + expectedCount; i++) {
                var foundItem = foundItems[i];
                expected.Should()
                    .ContainEquivalentOf(foundItem,
                        o => o.ExcludingSearchMatch().ExcludingBorders(),
                        $"iStart={iStart}, i={i}, count={expectedCount}");
                foundItem.IsFirstInGroup.Should().Be(i == iStart, $"iStart={iStart}, i={i}, count={expectedCount}");
                foundItem.IsLastInGroup.Should().Be(i == iStart + expectedCount - 1, $"iStart={iStart}, i={i}, count={expectedCount}");
            }
        }

        int GetExpectedCount()
            => new ICollection[] {
                expectedFriends,
                expectedStrangers,
                expectedJoinedGroups,
                expectedOtherGroups,
                expectedJoinedPlaces,
                expectedOtherPlaces,
                expectedEntries
            }.Sum(x => x.Count.Clamp(0, Constants.Search.DefaultPageSize));
    }

    private Task WaitUntilIndexed(
        string criteria,
        PlaceId? placeId,
        IReadOnlyCollection<AccountFull> expectedFriends,
        IReadOnlyCollection<AccountFull> expectedStrangers,
        IReadOnlyCollection<Chat> expectedJoinedGroups,
        IReadOnlyCollection<Chat> expectedOtherGroups,
        IReadOnlyCollection<ChatEntry> expectedEntries,
        IReadOnlyCollection<Place>? expectedJoinedPlaces = null,
        IReadOnlyCollection<Place>? expectedOtherPlaces = null)
        => TestExt.When(async () => {
                var uniqueCriteria = $"{criteria} {IsolationKey}";
                var owner = await Tester.GetOwnAccount();
                var friends = await Tester.FindPeople(uniqueCriteria, true, placeId);
                friends.Should()
                    .BeEquivalentTo(owner.BuildSearchResults(expectedFriends), o => o.ExcludingSearchMatch());
                var strangers = await Tester.FindPeople(uniqueCriteria, false, placeId);
                strangers.Should()
                    .BeEquivalentTo(owner.BuildSearchResults(expectedStrangers), o => o.ExcludingSearchMatch());
                var joinedGroups = await Tester.FindGroups(uniqueCriteria, true, placeId);
                joinedGroups.Should()
                    .BeEquivalentTo(owner.BuildSearchResults(expectedJoinedGroups), o => o.ExcludingSearchMatch());
                var otherGroups = await Tester.FindGroups(uniqueCriteria, false, placeId);
                otherGroups.Should()
                    .BeEquivalentTo(owner.BuildSearchResults(expectedOtherGroups), o => o.ExcludingSearchMatch());
                var entries = await Tester.FindEntries(uniqueCriteria, placeId);
                entries.Should().BeEquivalentTo(expectedEntries.BuildSearchResults([criteria, IsolationKey]), o => o.ExcludingSearchMatch());
                if (placeId is null) {
                    var joinedPlaces = await Tester.FindPlaces(uniqueCriteria, true);
                    joinedPlaces.Should()
                        .BeEquivalentTo(owner.BuildSearchResults(expectedJoinedPlaces), o => o.ExcludingSearchMatch());
                    var otherPlaces = await Tester.FindPlaces(uniqueCriteria, false);
                    otherPlaces.Should()
                        .BeEquivalentTo(owner.BuildSearchResults(expectedOtherPlaces), o => o.ExcludingSearchMatch());
                }
            },
            Intervals.Fixed(TimeSpan.FromSeconds(0.5)),
            TimeSpan.FromSeconds(30));

    private Task<IReadOnlyList<FoundItem>> GetSearchResults(int expectedCount)
        => TestsExt.When(async () => {
                var results = await SearchUI.GetSearchResults();
                results.Should().HaveCount(expectedCount);
                return results;
            },
            Intervals.Fixed(TimeSpan.FromSeconds(0.5)),
            TimeSpan.FromSeconds(10));

    // Nested types
}
