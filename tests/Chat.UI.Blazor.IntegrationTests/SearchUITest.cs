using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.IntegrationTests;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;
using ActualLab.Mathematics;
using SearchUI = ActualChat.UI.Blazor.App.Services.SearchUI;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(SearchUICollection))]
public class SearchUITest(SearchAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<SearchAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester { get; } = fixture.AppHost.NewBlazorTester(@out);
    private string UniquePart { get; } = UniqueNames.Prefix();
    private SearchUI SearchUI => Tester.ScopedAppServices.ChatUIHub().SearchUI;

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldFindOnCriteriaChange()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var groups = await Tester.CreateGroupContacts(bob, places, uniquePart: UniquePart);
        var people = await Tester.CreateUserContacts(bob, places, UniquePart);
        var entries = await Tester.CreateEntries(bob, groups, people, UniquePart);

        // act
        await Tester.SignIn(bob);
        var expectedFriends = people.Friends().Where(x => x.FullName.OrdinalIgnoreCaseContains("one")).ToArray();
        var expectedStrangers = people.Strangers().Where(x => x.FullName.OrdinalIgnoreCaseContains("one")).ToArray();
        var expectedJoinedGroups = groups.Joined().Where(x => x.Title.OrdinalIgnoreCaseContains("one")).ToArray();
        var expectedOtherGroups = groups.OtherPublic().Where(x => x.Title.OrdinalIgnoreCaseContains("one")).ToArray();
        var expectedJoinedPlaces = places.Joined().Where(x => x.Title.OrdinalIgnoreCaseContains("one")).ToArray();
        var expectedOtherPlaces = places.OtherPublic().Where(x => x.Title.OrdinalIgnoreCaseContains("one")).ToArray();
        var expectedEntries = entries.Accessible().ToArray();
        await WaitUntilIndexed("one", expectedFriends, expectedStrangers, expectedJoinedGroups, expectedOtherGroups, expectedJoinedPlaces, expectedOtherPlaces);
        SearchUI.Text.Value = $"{UniquePart} one";

        // assert
        var expectedTotalCount = GetExpectedTotalCount();
        var foundItems = await GetSearchResults(expectedTotalCount);
        AssertFoundItems(0, bob.BuildFoundContacts(false, expectedFriends).ToList());
        AssertFoundItems(3, bob.BuildFoundContacts(false, expectedJoinedGroups).ToList());
        AssertFoundItems(6, bob.BuildFoundContacts(false, expectedJoinedPlaces).ToList());
        AssertFoundItems(8, expectedEntries.BuildFoundEntries().ToArray());
        AssertFoundItems(11,
            bob.BuildFoundContacts(true, expectedStrangers).ToList(),
            bob.BuildFoundContacts(true, expectedOtherGroups).ToList(),
            bob.BuildFoundContacts(true, expectedOtherPlaces).ToList());

        // act
        SearchUI.PlaceId.Value = places.JoinedPrivatePlace2().Id;
        SearchUI.Text.Value = $"{UniquePart} one";

        // assert;
        expectedFriends = [people.Friend1FromPrivatePlace2()];
        expectedStrangers = [people.Stranger1FromPrivatePlace2()];
        expectedJoinedGroups = [
            groups.JoinedPrivatePlace2JoinedPrivateChat1(),
            groups.JoinedPrivatePlace2JoinedPublicChat1(),
        ];
        expectedOtherGroups = [];
        expectedJoinedPlaces = [];
        expectedOtherPlaces = [];
        expectedTotalCount = GetExpectedTotalCount();
        foundItems = await GetSearchResults(expectedTotalCount);
        AssertFoundItems(0, bob.BuildFoundContacts(false, expectedFriends).ToList());
        AssertFoundItems(1, bob.BuildFoundContacts(false, expectedJoinedGroups).ToList());
        AssertFoundItems(3, expectedEntries.BuildFoundEntries().ToArray());
        AssertFoundItems(6,
            bob.BuildFoundContacts(true, expectedStrangers).ToList(),
            bob.BuildFoundContacts(true, expectedOtherGroups).ToList());
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

        int GetExpectedTotalCount()
        {
            return expectedFriends.Length.Clamp(0, Constants.Search.DefaultPageSize)
                + expectedStrangers.Length.Clamp(0, Constants.Search.DefaultPageSize)
                + expectedJoinedGroups.Length.Clamp(0, Constants.Search.DefaultPageSize)
                + expectedOtherGroups.Length.Clamp(0, Constants.Search.DefaultPageSize)
                + expectedJoinedPlaces.Length.Clamp(0, Constants.Search.DefaultPageSize)
                + expectedOtherPlaces.Length.Clamp(0, Constants.Search.DefaultPageSize)
                + expectedEntries.Length.Clamp(0, Constants.Search.DefaultPageSize);
        }
    }

    private Task WaitUntilIndexed(
        string criteria,
        AccountFull[] expectedFriends,
        AccountFull[] expectedStrangers,
        Chat[] expectedJoinedGroups,
        Chat[] expectedOtherGroups,
        Place[] expectedJoinedPlaces,
        Place[] expectedOtherPlaces)
        => TestExt.When(async () => {
                var owner = await Tester.GetOwnAccount();
                var uniqueCriteria = criteria;
                var friends = await Tester.FindPeople(uniqueCriteria, true);
                friends.Should().BeEquivalentTo(owner.BuildSearchResults(expectedFriends), o => o.ExcludingSearchMatch());
                var strangers = await Tester.FindPeople(uniqueCriteria, false);
                strangers.Should().BeEquivalentTo(owner.BuildSearchResults(expectedStrangers), o => o.ExcludingSearchMatch());
                var joinedGroups = await Tester.FindGroups(uniqueCriteria, true);
                joinedGroups.Should()
                    .BeEquivalentTo(owner.BuildSearchResults(expectedJoinedGroups), o => o.ExcludingSearchMatch());
                var otherGroups = await Tester.FindGroups(uniqueCriteria, false);
                otherGroups.Should()
                    .BeEquivalentTo(owner.BuildSearchResults(expectedOtherGroups), o => o.ExcludingSearchMatch());
                var joinedPlaces = await Tester.FindPlaces(uniqueCriteria, true);
                joinedPlaces.Should()
                    .BeEquivalentTo(owner.BuildSearchResults(expectedJoinedPlaces), o => o.ExcludingSearchMatch());
                var otherPlaces = await Tester.FindPlaces(uniqueCriteria, false);
                otherPlaces.Should()
                    .BeEquivalentTo(owner.BuildSearchResults(expectedOtherPlaces), o => o.ExcludingSearchMatch());
            },
            TimeSpan.FromSeconds(30));

    private async Task<List<FoundItem>> GetSearchResults(int expectedCount)
    {
        IReadOnlyList<FoundItem> results = [];
        await TestExt.When(async () => {
                results = await SearchUI.GetSearchResults();
                results.Should().HaveCount(expectedCount);
            },
            TimeSpan.FromSeconds(10));
        return results.ToList();
    }
}
