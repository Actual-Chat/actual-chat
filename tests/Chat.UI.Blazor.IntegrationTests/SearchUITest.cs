using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.Users;
using ActualLab.Mathematics;
using SearchUI = ActualChat.UI.Blazor.App.Services.SearchUI;

namespace ActualChat.UI.Blazor.App.IntegrationTests;

[Collection(nameof(SearchUICollection))]
[Trait("Category", "Slow")]
public class SearchUITest(SearchAppHostFixture fixture, ITestOutputHelper @out)
    : SharedDbLocalAppHostTestBase<SearchAppHostFixture>(fixture, @out)
{
    private BlazorTester _tester = null!;
    private SearchUI _searchUI = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewBlazorTester(Out);
        _searchUI = _tester.ScopedAppServices.ChatUIHub().SearchUI;
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldFindOnCriteriaChange()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob);
        var chats = await _tester.CreateGroupContacts(bob, places);
        var people = await _tester.CreateUserContacts(bob, places);

        // act
        await _tester.SignIn(bob);
        var expectedFriends = people.Friends().Where(x => x.FullName.OrdinalIgnoreCaseContains("one")).ToArray();
        var expectedStrangers = people.Strangers().Where(x => x.FullName.OrdinalIgnoreCaseContains("one")).ToArray();
        var expectedJoinedGroups = chats.Joined().Where(x => x.Title.OrdinalIgnoreCaseContains("one")).ToArray();
        var expectedOtherGroups = chats.OtherPublic().Where(x => x.Title.OrdinalIgnoreCaseContains("one")).ToArray();
        var expectedJoinedPlaces = places.Joined().Where(x => x.Title.OrdinalIgnoreCaseContains("one")).ToArray();
        var expectedOtherPlaces = places.OtherPublic().Where(x => x.Title.OrdinalIgnoreCaseContains("one")).ToArray();
        await WaitUntilIndexed("one", expectedFriends, expectedStrangers, expectedJoinedGroups, expectedOtherGroups, expectedJoinedPlaces, expectedOtherPlaces);
        _searchUI.Text.Value = "one";

        // assert
        var expectedTotalCount = expectedFriends.Length.Clamp(0, Constants.Search.ContactSearchDefaultPageSize)
            + expectedStrangers.Length.Clamp(0, Constants.Search.ContactSearchDefaultPageSize)
            + expectedJoinedGroups.Length.Clamp(0, Constants.Search.ContactSearchDefaultPageSize)
            + expectedOtherGroups.Length.Clamp(0, Constants.Search.ContactSearchDefaultPageSize)
            + expectedJoinedPlaces.Length.Clamp(0, Constants.Search.ContactSearchDefaultPageSize)
            + expectedOtherPlaces.Length.Clamp(0, Constants.Search.ContactSearchDefaultPageSize);
        var foundContacts = await GetSearchResults(expectedTotalCount);
        AssertFoundContacts(0,
            bob.BuildFoundContacts(expectedFriends).ToList(),
            bob.BuildFoundContacts(expectedStrangers).ToList());
        AssertFoundContacts(6,
            bob.BuildFoundContacts(expectedJoinedGroups).ToList(),
            bob.BuildFoundContacts(expectedOtherGroups).ToList());
        AssertFoundContacts(12,
            bob.BuildFoundContacts(expectedJoinedPlaces).ToList(),
            bob.BuildFoundContacts(expectedOtherPlaces).ToList());

        void AssertFoundContacts(int iStart, IReadOnlyCollection<FoundContact> expectedOwn, IReadOnlyCollection<FoundContact> expectedOther)
        {
            var expected = expectedOwn.Concat(expectedOther).ToList();
            var expectedCount = expectedOwn.Count.Clamp(0, Constants.Search.ContactSearchDefaultPageSize) + expectedOther.Count.Clamp(0, Constants.Search.ContactSearchDefaultPageSize);
            for (int i = iStart; i < iStart + expectedCount; i++) {
                expected.Should().ContainEquivalentOf(foundContacts[i], o => o.ExcludingSearchMatch().ExcludingBorders());
                foundContacts[i].IsFirstInGroup.Should().Be(i == iStart, $"iStart={i}, i={i}, count={expectedCount}");
                foundContacts[i].IsLastInGroup.Should().Be(i == iStart + expectedCount - 1, $"iStart={i}, i={i}, count={expectedCount}");
            }
        }
    }

    private Task WaitUntilIndexed(
        string criteria,
        AccountFull[] expectedFriends,
        AccountFull[] expectedStrangers,
        Chat.Chat[] expectedJoinedGroups,
        Chat.Chat[] expectedOtherGroups,
        Place[] expectedJoinedPlaces,
        Place[] expectedOtherPlaces)
        => TestExt.When(async () => {
                var owner = await _tester.GetOwnAccount();
                var friends = await _tester.FindPeople(criteria, true);
                friends.Should().BeEquivalentTo(owner.BuildSearchResults(expectedFriends), o => o.ExcludingSearchMatch());
                var strangers = await _tester.FindPeople(criteria, false);
                strangers.Should().BeEquivalentTo(owner.BuildSearchResults(expectedStrangers), o => o.ExcludingSearchMatch());
                var joinedGroups = await _tester.FindGroups(criteria, true);
                joinedGroups.Should()
                    .BeEquivalentTo(owner.BuildSearchResults(expectedJoinedGroups), o => o.ExcludingSearchMatch());
                var otherGroups = await _tester.FindGroups(criteria, false);
                otherGroups.Should()
                    .BeEquivalentTo(owner.BuildSearchResults(expectedOtherGroups), o => o.ExcludingSearchMatch());
                var joinedPlaces = await _tester.FindPlaces(criteria, true);
                joinedPlaces.Should()
                    .BeEquivalentTo(owner.BuildSearchResults(expectedJoinedPlaces), o => o.ExcludingSearchMatch());
                var otherPlaces = await _tester.FindPlaces(criteria, false);
                otherPlaces.Should()
                    .BeEquivalentTo(owner.BuildSearchResults(expectedOtherPlaces), o => o.ExcludingSearchMatch());
            },
            TimeSpan.FromSeconds(30));

    private async Task<List<FoundContact>> GetSearchResults(int expectedCount)
    {
        IReadOnlyList<FoundContact> results = [];
        await TestExt.When(async () => {
                results = await _searchUI.GetContactSearchResults();
                results.Should().HaveCount(expectedCount);
            },
            TimeSpan.FromSeconds(10));
        return results.ToList();
    }
}
