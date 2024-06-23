using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualLab.Generators;
using static ActualChat.Testing.Host.Assertion.AssertOptionsExt;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection))]
[Trait("Category", "Slow")]
public class PlaceContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
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
    public async Task ShouldFindPlacesOnly()
    {
        // arrange
        var bob = await _tester.SignInAsBob(RandomStringGenerator.Default.Next());
        await _tester.SignInAsAlice();
        var privateChat = await CreateChat(_tester, false, "Private non-place chat 1 one");
        var publicChat = await CreateChat(_tester, true, "Public non-place chat 1 one");
        var joinedPrivatePlace1 = await _tester.CreatePlace(false, "Private Place 1 one with Bob as member", bob);
        var joinedPrivatePlace2 = await _tester.CreatePlace(false, "Private Place 2 two with Bob as member", bob);
        var otherPrivatePlace1 = await _tester.CreatePlace(false, "Private Place 1 one without Bob as member");
        var otherPrivatePlace2 = await _tester.CreatePlace(false, "Private Place 2 two without Bob as member");
        var joinedPrivatePlace1PrivateChat = await CreateChat(_tester, false, "Private Place 1 one with Bob as member - private chat", joinedPrivatePlace1.Id, bob);
        var joinedPrivatePlace2PrivateChat = await CreateChat(_tester, false, "Private Place 2 two with Bob as member - private chat", joinedPrivatePlace2.Id, bob);
        var otherPrivatePlace1PrivateChat = await CreateChat(_tester, false, "Private Place 1 one without Bob as member - private chat", otherPrivatePlace1.Id);
        var otherPrivatePlace2PrivateChat = await CreateChat(_tester, false, "Private Place 1 one without Bob as member - private chat", otherPrivatePlace2.Id);
        var joinedPrivatePlace1PublicChat = await CreateChat(_tester, true, "Private Place 1 one with Bob as member - public chat", joinedPrivatePlace1.Id);
        var joinedPrivatePlace2PublicChat = await CreateChat(_tester, true, "Private Place 2 two with Bob as member - public chat", joinedPrivatePlace2.Id);
        var otherPrivatePlace1PublicChat = await CreateChat(_tester, true, "Private Place 1 one without Bob as member - public chat", otherPrivatePlace1.Id);
        var otherPrivatePlace2PublicChat = await CreateChat(_tester, true, "Private Place 2 two without Bob as member - public chat", otherPrivatePlace2.Id);
        var joinedPublicPlace1 = await _tester.CreatePlace(true, "Public Place 1 one with Bob as member", bob);
        var joinedPublicPlace2 = await _tester.CreatePlace(true, "Public Place 2 two with Bob as member", bob);
        var otherPublicPlace1 = await _tester.CreatePlace(true, "Public Place 1 one without Bob as member");
        var otherPublicPlace2 = await _tester.CreatePlace(true, "Public Place 2 two without Bob as member");
        var joinedPublicPlace1PrivateChat = await CreateChat(_tester, false, "Public Place 1 one with Bob as member - private chat", joinedPublicPlace1.Id, bob);
        var joinedPublicPlace2PrivateChat = await CreateChat(_tester, false, "Public Place 2 two with Bob as member - private chat", joinedPublicPlace2.Id, bob);
        var otherPublicPlace1PrivateChat = await CreateChat(_tester, false, "Public Place 1 one without Bob as member - private chat", otherPublicPlace1.Id);
        var otherPublicPlace2PrivateChat = await CreateChat(_tester, false, "Public Place 1 one without Bob as member - private chat", otherPublicPlace2.Id);
        var joinedPublicPlace1PublicChat = await CreateChat(_tester, true, "Public Place 1 one with Bob as member - public chat", joinedPublicPlace1.Id);
        var joinedPublicPlace2PublicChat = await CreateChat(_tester, true, "Public Place 2 two with Bob as member - public chat", joinedPublicPlace2.Id);
        var otherPublicPlace1PublicChat = await CreateChat(_tester, true, "Public Place 1 one without Bob as member - public chat", otherPublicPlace1.Id);
        var otherPublicPlace2PublicChat = await CreateChat(_tester, true, "Public Place 2 two without Bob as member - public chat", otherPublicPlace2.Id);

        // act
        var updates = IndexedContactUtil.BuildChatContacts(
            [
                joinedPrivatePlace1,
                joinedPrivatePlace2,
                otherPrivatePlace1,
                otherPrivatePlace2,
                joinedPublicPlace1,
                joinedPublicPlace2,
                otherPublicPlace1,
                otherPublicPlace2,
            ],
            privateChat,
            publicChat,
            joinedPrivatePlace1PrivateChat,
            joinedPrivatePlace2PrivateChat,
            otherPrivatePlace1PrivateChat,
            otherPrivatePlace2PrivateChat,
            joinedPublicPlace1PrivateChat,
            joinedPrivatePlace1PublicChat,
            joinedPrivatePlace2PublicChat,
            otherPrivatePlace1PublicChat,
            otherPrivatePlace2PublicChat,
            joinedPublicPlace1PrivateChat,
            joinedPublicPlace2PrivateChat,
            otherPublicPlace1PrivateChat,
            otherPublicPlace2PrivateChat,
            joinedPublicPlace1PublicChat,
            joinedPublicPlace2PublicChat,
            otherPublicPlace1PublicChat,
            otherPublicPlace2PublicChat);
        await _commander.Call(new SearchBackend_ChatContactBulkIndex(updates, ApiArray<IndexedChatContact>.Empty));
        await _commander.Call(new SearchBackend_Refresh(refreshChats: true));

        // assert
        await _tester.SignIn(bob.User);
        var searchResults = await _tester.FindPlaces("place", true);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(
                    (joinedPrivatePlace1, [(8, 13)]),
                    (joinedPrivatePlace2, [(8, 13)]),
                    (joinedPublicPlace1, [(7, 12)]),
                    (joinedPublicPlace2, [(7, 12)])),
                o => o.ExcludingRank());

        searchResults = await _tester.FindPlaces("place", false);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(
                    (otherPublicPlace1, [(7, 12)]),
                    (otherPublicPlace2, [(7, 12)])),
                o => o.ExcludingRank());

        searchResults = await _tester.FindPlaces("one", true);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(
                    (joinedPrivatePlace1, [(16, 19)]),
                    (joinedPublicPlace1, [(15, 18)])),
                o => o.ExcludingRank());

        searchResults = await _tester.FindPlaces("one", false);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(
                    (otherPublicPlace1, [(15, 18)])),
                o => o.ExcludingRank());

        searchResults = await _tester.FindPlaces("two", true);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(
                    (joinedPrivatePlace2, [(16, 19)]),
                    (joinedPublicPlace2, [(15, 18)])),
                o => o.ExcludingRank());

        searchResults = await _tester.FindPlaces("two", false);
        searchResults.Should()
            .BeEquivalentTo(
                bob.BuildSearchResults(
                    (otherPublicPlace2, [(15, 18)])),
                o => o.ExcludingRank());
    }

    // Private methods

    private async Task<Chat.Chat> CreateChat(WebClientTester tester, bool isPublic, string title, PlaceId placeId = default, AccountFull? userToInvite = null)
    {
        var (id, _) = await tester.CreateChat(isPublic, title, placeId);
        if (userToInvite != null)
            await tester.InviteToChat(id, userToInvite.Id);
        return await tester.Chats.Get(tester.Session, id, CancellationToken.None).Require();
    }
}
