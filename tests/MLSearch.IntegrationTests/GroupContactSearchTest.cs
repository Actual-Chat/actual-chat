using ActualChat.Search;
using ActualChat.Testing.Host;
using static ActualChat.Testing.Host.Assertion.AssertOptionsExt;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
public class GroupContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
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
    public async Task ShouldFindGroups()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var chats = await Tester.CreateGroupContacts(bob, places, uniquePart: UniquePart);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(chats.JoinedGroups2());
        var searchResults = await Find("chat", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPublicChat1(), UniquePart, [(19, 23)]), o => o.ExcludingRank());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPublicPlace1JoinedPublicChat1(), UniquePart, [(48, 52)]), o => o.ExcludingRank());

        expected = bob.BuildSearchResults(chats.OtherPublicGroups2());
        searchResults = await Find("chat", false, null, expected.Count);
        searchResults.Should()
            .BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.OtherPublicChat1(), UniquePart, [(19, 23)]), o => o.ExcludingRank());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.OtherPublicPlace1OtherPublicChat1(), UniquePart, [(51, 55)]), o => o.ExcludingRank());

        expected = bob.BuildSearchResults(
            chats.JoinedPublicPlace1JoinedPrivateChat1(),
            chats.JoinedPublicPlace1JoinedPrivateChat2(),
            chats.JoinedPublicPlace2JoinedPrivateChat1(),
            chats.JoinedPrivatePlace1JoinedPublicChat1(),
            chats.JoinedPrivatePlace1JoinedPublicChat2(),
            chats.JoinedPrivatePlace2JoinedPublicChat1());
        searchResults = await Find("public one priva", true, null, expected.Count);
        searchResults.Should()
            .BeEquivalentTo(expected,
                o => o.ExcludingSearchMatch());

        expected = bob.BuildSearchResults(chats.OtherPublicChat1(),
            chats.OtherPublicPlace1OtherPublicChat1(),
            chats.OtherPublicPlace1OtherPublicChat2(),
            chats.OtherPublicPlace2OtherPublicChat1());
        searchResults = await Find("public one", false, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindGroupsInPlaceByPrefix()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var chats = await Tester.CreateGroupContacts(bob, places, uniquePart: UniquePart);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(chats.JoinedPrivatePlace1JoinedChats());
        var searchResults = await Find("ch", true, places.JoinedPrivatePlace1().Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPrivatePlace1JoinedPrivateChat2(), UniquePart, [(50, 54)]), o => o.ExcludingRank());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPrivatePlace1JoinedPublicChat1(), UniquePart, [(49, 53)]), o => o.ExcludingRank());

        searchResults = await Find("ch", false, places.JoinedPrivatePlace1().Id, 0);
        searchResults.Should().BeEmpty("private groups are not visible while public groups are 'joined' automatically");

        // assert
        expected = bob.BuildSearchResults(chats.JoinedPublicPlace1JoinedChats());
        searchResults = await Find("ch", true, places.JoinedPublicPlace1().Id, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPublicPlace1JoinedPrivateChat2(), UniquePart, [(49, 53)]), o => o.ExcludingRank());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPublicPlace1JoinedPublicChat1(), UniquePart, [(48, 52)]), o => o.ExcludingRank());

        searchResults = await Find("ch", false, places.JoinedPublicPlace1().Id, 0);
        searchResults.Should().BeEmpty("private groups are not visible while public groups are 'joined' automatically");
    }

    [Fact]
    public async Task ShouldFindUpdatedGroups()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var alice = await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var chats = await Tester.CreateGroupContacts(bob, places, uniquePart: UniquePart);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(chats.JoinedGroups2());
        var searchResults = await Find("chat", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act
        await Tester.SignIn(alice);
        var updatedChat = await Tester.UpdateChat(chats.JoinedPrivatePlace1JoinedPrivateChat1().Id, $"{UniquePart} bbb");
        await Tester.SignIn(bob);

        // assert
        expected = bob.BuildSearchResults(chats.JoinedGroups2().Except([chats.JoinedPrivatePlace1JoinedPrivateChat1()]));
        searchResults = await Find("chat", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // assert
        searchResults = await Find("bbb", true, null, 1);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults([updatedChat]), o => o.ExcludingSearchMatch());
    }

    [Fact(Skip = "Flaky")]
    public async Task ShouldNotFindDeletedGroups()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var alice = await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var chats = await Tester.CreateGroupContacts(bob, places, uniquePart: UniquePart);
        await Tester.SignIn(bob);

        // act, assert
        var expected = bob.BuildSearchResults(chats.JoinedGroups2());
        var searchResults = await Find("chat", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());

        // act
        await Tester.SignIn(alice);
        await Tester.DeleteChat(chats.JoinedPrivatePlace1JoinedPrivateChat1().Id);
        await Tester.SignIn(bob);

        // assert
        expected = bob.BuildSearchResults(chats.JoinedGroups2().Except([chats.JoinedPrivatePlace1JoinedPrivateChat1()]));
        searchResults = await Find("chat", true, null, expected.Count);
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    private Task<ContactSearchResult[]> Find(string criteria, bool own, PlaceId? placeId, int expectedCount)
        => TestsExt.When(async () => {
                var groups = await Tester.FindGroups($"{UniquePart} {criteria}", own, placeId, 50);
                groups.Should().HaveCount(expectedCount);
                return groups;
            },
            TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(20));
}
