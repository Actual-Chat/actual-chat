using ActualChat.Testing.Host;
using ActualLab.Generators;
using static ActualChat.Testing.Host.Assertion.AssertOptionsExt;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection))]
[Trait("Category", "Slow")]
public class GroupContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
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
    public async Task ShouldFindGroups()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob);
        var chats = await _tester.CreateGroupContacts(bob, places);

        // act
        var updates = IndexedContactUtil.BuildChatContacts(places.Values, chats.Values.ToArray());
        await _commander.Call(new SearchBackend_ChatContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(refreshGroups: true));
        await _tester.SignIn(bob);

        // assert
        var searchResults = await _tester.FindGroups("chat", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(chats.JoinedGroups2().ToArray()), o => o.ExcludingSearchMatch());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPublicChat1(), [(19, 23)]), o => o.ExcludingRank());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPublicPlace1JoinedPublicChat1(), [(48, 52)]), o => o.ExcludingRank());

        searchResults = await _tester.FindGroups("chat", false);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(chats.OtherPublicGroups2().ToArray()), o => o.ExcludingSearchMatch());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.OtherPublicChat1(), [(19, 23)]), o => o.ExcludingRank());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.OtherPublicPlace1OtherPublicChat1(), [(51, 55)]), o => o.ExcludingRank());

        searchResults = await _tester.FindGroups("public one priva", true);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(chats.JoinedPublicPlace1JoinedPrivateChat1(),
                    chats.JoinedPublicPlace1JoinedPrivateChat2(),
                    chats.JoinedPublicPlace2JoinedPrivateChat1(),
                    chats.JoinedPrivatePlace1JoinedPublicChat1(),
                    chats.JoinedPrivatePlace1JoinedPublicChat2(),
                    chats.JoinedPrivatePlace2JoinedPublicChat1()),
                o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindGroupsInPlaceByPrefix()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob);
        var chats = await _tester.CreateGroupContacts(bob, places);

        // act
        var updates = IndexedContactUtil.BuildChatContacts(places.Values, chats.Values.ToArray());
        await _commander.Call(new SearchBackend_ChatContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(refreshGroups: true));
        await _tester.SignIn(bob);

        // assert
        var searchResults = await _tester.FindGroups("ch", true, places.JoinedPrivatePlace1().Id);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(chats.JoinedPrivatePlace1JoinedChats().ToArray()), o => o.ExcludingSearchMatch());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPrivatePlace1JoinedPrivateChat2(), [(50, 54)]), o => o.ExcludingRank());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPrivatePlace1JoinedPublicChat1(), [(49, 53)]), o => o.ExcludingRank());

        searchResults = await _tester.FindGroups("ch", false, places.JoinedPrivatePlace1().Id);
        searchResults.Should().BeEmpty("private groups are not visible while public groups are 'joined' automatically");

        // assert
        searchResults = await _tester.FindGroups("ch", true, places.JoinedPublicPlace1().Id);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(chats.JoinedPublicPlace1JoinedChats().ToArray()), o => o.ExcludingSearchMatch());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPublicPlace1JoinedPrivateChat2(), [(49, 53)]), o => o.ExcludingRank());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPublicPlace1JoinedPublicChat1(), [(48, 52)]), o => o.ExcludingRank());

        searchResults = await _tester.FindGroups("ch", false, places.JoinedPublicPlace1().Id);
        searchResults.Should().BeEmpty("private groups are not visible while public groups are 'joined' automatically");
    }

    [Fact]
    public async Task ShouldFindUpdatedGroups()
    {
        // arrange
        var bob = await _tester.SignInAsBob(RandomStringGenerator.Default.Next());
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob);
        var chats = await _tester.CreateGroupContacts(bob, places);

        // act
        var updates = IndexedContactUtil.BuildChatContacts(places.Values, chats.Values.ToArray());
        await _commander.Call(new SearchBackend_ChatContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(refreshGroups: true));
        await _tester.SignIn(bob);

        // assert
        var searchResults = await _tester.FindGroups("chat", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(chats.JoinedGroups2().ToArray()), o => o.ExcludingSearchMatch());

        // act
        var updatedChat = chats.JoinedPrivatePlace1JoinedPrivateChat1() with { Title = "bbb" };
        updates = IndexedContactUtil.BuildChatContacts(places.Values, updatedChat);
        await _commander.Call(new SearchBackend_ChatContactBulkIndex(updates, ApiArray<IndexedGroupChatContact>.Empty));
        await _commander.Call(new SearchBackend_Refresh(refreshGroups: true));

        // assert
        searchResults = await _tester.FindGroups("chat", true);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(chats.JoinedGroups2()
                    .Except([chats.JoinedPrivatePlace1JoinedPrivateChat1()])
                    .ToArray()),
                o => o.ExcludingSearchMatch());

        // assert
        searchResults = await _tester.FindGroups("bbb", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(updatedChat), o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldNotFindDeletedGroups()
    {
        // arrange
        var bob = await _tester.SignInAsBob(RandomStringGenerator.Default.Next());
        await _tester.SignInAsAlice();
        var places = await _tester.CreatePlaceContacts(bob);
        var chats = await _tester.CreateGroupContacts(bob, places);

        // act
        var updates = IndexedContactUtil.BuildChatContacts(places.Values, chats.Values.ToArray());
        await _commander.Call(new SearchBackend_ChatContactBulkIndex(updates, []));
        await _commander.Call(new SearchBackend_Refresh(refreshGroups: true));
        await _tester.SignIn(bob);

        // assert
        var searchResults = await _tester.FindGroups("chat", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(chats.JoinedGroups2().ToArray()), o => o.ExcludingSearchMatch());

        // act
        var deleted = IndexedContactUtil.BuildChatContacts(places.Values, chats.JoinedPrivatePlace1JoinedPrivateChat1());
        await _commander.Call(new SearchBackend_ChatContactBulkIndex([], deleted));
        await _commander.Call(new SearchBackend_Refresh(refreshGroups: true));

        // assert
        searchResults = await _tester.FindGroups("chat", true);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(chats.JoinedGroups2()
                    .Except([chats.JoinedPrivatePlace1JoinedPrivateChat1()])
                    .ToArray()),
                o => o.ExcludingSearchMatch());
    }
}
