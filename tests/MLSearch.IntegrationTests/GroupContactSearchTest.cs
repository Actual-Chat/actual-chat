using ActualChat.MLSearch.IntegrationTests.Util;
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

        // act
        var updates = IndexedContactUtil.BuildChatContacts(places.Values, chats.Values.ToArray());
        await Commander.Call(new SearchBackend_ChatContactBulkIndex(updates, []));
        await Commander.Call(new SearchBackend_Refresh(refreshGroups: true));
        await Tester.SignIn(bob);

        // assert
        var searchResults = await Find("chat", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(chats.JoinedGroups2().ToArray()), o => o.ExcludingSearchMatch());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPublicChat1(), [(19, 23), (31, 36)]), o => o.ExcludingRank());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPublicPlace1JoinedPublicChat1(), [(49, 53), (61, 66)]), o => o.ExcludingRank());

        searchResults = await Find("chat", false);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(chats.OtherPublicGroups2().ToArray()), o => o.ExcludingSearchMatch());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.OtherPublicChat1(), [(19, 23), (31, 36)]), o => o.ExcludingRank());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.OtherPublicPlace1OtherPublicChat1(), [(52, 56), (64, 69)]), o => o.ExcludingRank());

        searchResults = await Find("public one priva", true);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(chats.JoinedPublicPlace1JoinedPrivateChat1(),
                    chats.JoinedPublicPlace1JoinedPrivateChat2(),
                    chats.JoinedPublicPlace2JoinedPrivateChat1(),
                    chats.JoinedPrivatePlace1JoinedPublicChat1(),
                    chats.JoinedPrivatePlace1JoinedPublicChat2(),
                    chats.JoinedPrivatePlace2JoinedPublicChat1()),
                o => o.ExcludingSearchMatch());

        searchResults = await Find("public one", false);
        searchResults.Should()
            .BeEquivalentTo(bob.BuildSearchResults(
                    chats.OtherPublicChat1(),
                    chats.OtherPublicPlace1OtherPublicChat1(),
                    chats.OtherPublicPlace1OtherPublicChat2(),
                    chats.OtherPublicPlace2OtherPublicChat1()),
                o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindGroupsInPlaceByPrefix()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var chats = await Tester.CreateGroupContacts(bob, places, uniquePart: UniquePart);

        // act
        var updates = IndexedContactUtil.BuildChatContacts(places.Values, chats.Values.ToArray());
        await Commander.Call(new SearchBackend_ChatContactBulkIndex(updates, []));
        await Commander.Call(new SearchBackend_Refresh(refreshGroups: true));
        await Tester.SignIn(bob);

        // assert
        var searchResults = await Find("ch", true, places.JoinedPrivatePlace1().Id);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(chats.JoinedPrivatePlace1JoinedChats().ToArray()), o => o.ExcludingSearchMatch());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPrivatePlace1JoinedPrivateChat2(), [(51, 55),(62, 67)]), o => o.ExcludingRank());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPrivatePlace1JoinedPublicChat1(), [(50, 54), (62, 67)]), o => o.ExcludingRank());

        searchResults = await Find("ch", false, places.JoinedPrivatePlace1().Id);
        searchResults.Should().BeEmpty("private groups are not visible while public groups are 'joined' automatically");

        // assert
        searchResults = await Find("ch", true, places.JoinedPublicPlace1().Id);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(chats.JoinedPublicPlace1JoinedChats().ToArray()), o => o.ExcludingSearchMatch());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPublicPlace1JoinedPrivateChat2(), [(50, 54), (61, 66)]), o => o.ExcludingRank());
        searchResults.Should().ContainEquivalentOf(bob.BuildSearchResult(chats.JoinedPublicPlace1JoinedPublicChat1(), [(49, 53), (61, 66)]), o => o.ExcludingRank());

        searchResults = await Find("ch", false, places.JoinedPublicPlace1().Id);
        searchResults.Should().BeEmpty("private groups are not visible while public groups are 'joined' automatically");
    }

    [Fact]
    public async Task ShouldFindUpdatedGroups()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var chats = await Tester.CreateGroupContacts(bob, places, uniquePart: UniquePart);

        // act
        var updates = IndexedContactUtil.BuildChatContacts(places.Values, chats.Values.ToArray());
        await Commander.Call(new SearchBackend_ChatContactBulkIndex(updates, []));
        await Commander.Call(new SearchBackend_Refresh(refreshGroups: true));
        await Tester.SignIn(bob);

        // assert
        var searchResults = await Find("chat", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(chats.JoinedGroups2().ToArray()), o => o.ExcludingSearchMatch());

        // act
        var updatedChat = chats.JoinedPrivatePlace1JoinedPrivateChat1() with { Title = $"{UniquePart} bbb" };
        updates = IndexedContactUtil.BuildChatContacts(places.Values, updatedChat);
        await Commander.Call(new SearchBackend_ChatContactBulkIndex(updates, ApiArray<IndexedGroupChatContact>.Empty));
        await Commander.Call(new SearchBackend_Refresh(refreshGroups: true));

        // assert
        searchResults = await Find("chat", true);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(chats.JoinedGroups2()
                    .Except([chats.JoinedPrivatePlace1JoinedPrivateChat1()])
                    .ToArray()),
                o => o.ExcludingSearchMatch());

        // assert
        searchResults = await Find("bbb", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(updatedChat), o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldNotFindDeletedGroups()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var chats = await Tester.CreateGroupContacts(bob, places, uniquePart: UniquePart);

        // act
        var updates = IndexedContactUtil.BuildChatContacts(places.Values, chats.Values.ToArray());
        await Commander.Call(new SearchBackend_ChatContactBulkIndex(updates, []));
        await Commander.Call(new SearchBackend_Refresh(refreshGroups: true));
        await Tester.SignIn(bob);

        // assert
        var searchResults = await Find("chat", true);
        searchResults.Should().BeEquivalentTo(bob.BuildSearchResults(chats.JoinedGroups2().ToArray()), o => o.ExcludingSearchMatch());

        // act
        var deleted = IndexedContactUtil.BuildChatContacts(places.Values, chats.JoinedPrivatePlace1JoinedPrivateChat1());
        await Commander.Call(new SearchBackend_ChatContactBulkIndex([], deleted));
        await Commander.Call(new SearchBackend_Refresh(refreshGroups: true));

        // assert
        searchResults = await Find("chat", true);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(chats.JoinedGroups2()
                    .Except([chats.JoinedPrivatePlace1JoinedPrivateChat1()])
                    .ToArray()),
                o => o.ExcludingSearchMatch());
    }

    private Task<ApiArray<ContactSearchResult>> Find(string criteria, bool own, PlaceId? placeId = null)
        => Tester.FindGroups($"{UniquePart} {criteria}", own, placeId);
}
