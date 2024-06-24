using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.Users;
using ActualLab.Mathematics;
using SearchUI = ActualChat.Chat.UI.Blazor.Services.SearchUI;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(SearchUICollection))]
[Trait("Category", "Slow")]
public class SearchUITest(SearchAppHostFixture fixture, ITestOutputHelper @out)
    : SharedDbLocalAppHostTestBase<SearchAppHostFixture>(fixture, @out)
{
    private BlazorTester _tester = null!;
    private SearchUI _searchUI = null!;
    private ChatListUI _chatListUI = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewBlazorTester(Out);
        _searchUI = _tester.ScopedAppServices.ChatUIHub().SearchUI;
        _chatListUI = _tester.ScopedAppServices.ChatUIHub().ChatListUI;
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
        var foundContacts = await GetSearchResults(expectedFriends.Length.Clamp(0, 5)
            + expectedStrangers.Length.Clamp(0, 5)
            + expectedJoinedGroups.Length.Clamp(0, 5)
            + expectedOtherGroups.Length.Clamp(0, 5)
            + expectedJoinedPlaces.Length.Clamp(0, 5)
            + expectedOtherPlaces.Length.Clamp(0, 5));
        var expected = bob
            .BuildFoundContacts(expectedFriends)
            .ToList();
        foundContacts[0].IsFirstInGroup.Should().Be(true);
        for (int i = 0; i < 5; i++)
            expected.Should().ContainEquivalentOf(foundContacts[i], o => o.ExcludingSearchMatch().ExcludingBorders());

        // assert
        expected = bob
            .BuildFoundContacts(expectedStrangers)
            .ToList();
        for (int i = 5; i < 10; i++)
            expected.Should().ContainEquivalentOf(foundContacts[i], o => o.ExcludingSearchMatch().ExcludingBorders());

        // assert
        expected = bob
            .BuildFoundContacts(expectedJoinedGroups)
            .ToList();
        foundContacts[10].IsFirstInGroup.Should().Be(true);
        for (int i = 10; i < 15; i++)
            expected.Should().ContainEquivalentOf(foundContacts[i], o => o.ExcludingSearchMatch().ExcludingBorders());

        // assert
        expected = bob
            .BuildFoundContacts(expectedOtherGroups)
            .ToList();
        for (int i = 15; i < 19; i++)
            expected.Should().ContainEquivalentOf(foundContacts[i], o => o.ExcludingSearchMatch().ExcludingBorders());

        // assert
        expected = bob
            .BuildFoundContacts(expectedJoinedPlaces)
            .ToList();
        foundContacts[19].IsFirstInGroup.Should().Be(true);
        for (int i = 19; i < 21; i++)
            expected.Should().ContainEquivalentOf(foundContacts[i], o => o.ExcludingSearchMatch().ExcludingBorders());

        // assert
        expected = bob
            .BuildFoundContacts(expectedOtherPlaces)
            .ToList();
        for (int i = 21; i < 22; i++)
            expected.Should().ContainEquivalentOf(foundContacts[i], o => o.ExcludingSearchMatch().ExcludingBorders());

        // act
        _chatListUI.ActivateChatList(places.JoinedPrivatePlace1().Id);
        _searchUI.Text.Value = "one";

        // assert
        var expectedGroups = new [] {
            chats.JoinedPrivatePlace1JoinedPublicChat1(),
            chats.JoinedPrivatePlace1JoinedPublicChat2(),
            chats.JoinedPrivatePlace1JoinedPrivateChat1(),
            chats.JoinedPrivatePlace1JoinedPrivateChat2(),
        };
        var expectedPeople = new[] {
            people.Friend1FromPrivatePlace1(),
            people.Friend2FromPrivatePlace1(),
            people.Stranger1FromPrivatePlace1(),
            people.Stranger2FromPrivatePlace1(),
        };
        expected = bob.BuildFoundContacts(expectedGroups)
            .Concat(bob.BuildFoundContacts(expectedPeople))
            .ToList();
        foundContacts = await GetSearchResults(expected.Count);
        foundContacts.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch().ExcludingBorders());

        // act
        _chatListUI.ActivateChatList(places.JoinedPrivatePlace1().Id);
        _searchUI.Text.Value = "two";

        // assert
        expectedGroups = [
            chats.JoinedPrivatePlace1JoinedPublicChat2(),
            chats.JoinedPrivatePlace1JoinedPrivateChat2(),
        ];
        expectedPeople = [
            people.Friend2FromPrivatePlace1(),
            people.Stranger2FromPrivatePlace1(),
        ];
        expected = bob.BuildFoundContacts(expectedGroups)
            .Concat(bob.BuildFoundContacts(expectedPeople))
            .ToList();
        foundContacts = await GetSearchResults(expected.Count);
        foundContacts.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch().ExcludingBorders());
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
            TimeSpan.FromSeconds(30));
        return results.ToList();
    }
}
