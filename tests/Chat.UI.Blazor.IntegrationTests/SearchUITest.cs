using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Search;
using ActualChat.Testing.Host;
using ActualLab.Generators;
using SearchUI = ActualChat.Chat.UI.Blazor.Services.SearchUI;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(SearchUICollection))]
public class SearchUITest(SearchAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<SearchAppHostFixture>(fixture, @out)
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
        var bob = await _tester.SignInAsBob(RandomStringGenerator.Default.Next());
        var privateChat1 = await CreateChat(false, "Private non-place chat 1 one");
        var privateChat2 = await CreateChat(false, "Private non-place chat 2 two");
        var publicChat1 = await CreateChat(true, "Public non-place chat 1 one");
        var publicChat2 = await CreateChat(true, "Public non-place chat 2 two");
        var privatePlace = await CreatePlace(false, "Bob's private Place");
        var privatePlacePrivateChat1 = await CreateChat(false, "Private place private chat 1 one", privatePlace.Id);
        var privatePlacePrivateChat2 = await CreateChat(false, "Private place private chat 2 two", privatePlace.Id);
        var privatePlacePublicChat1 = await CreateChat(true, "Private place public chat 1 one", privatePlace.Id);
        var privatePlacePublicChat2 = await CreateChat(true, "Private place public chat 2 two", privatePlace.Id);
        var publicPlace = await CreatePlace(true, "Bob's public Place");
        var publicPlacePrivateChat1 = await CreateChat(false, "Public place private chat 1 one", publicPlace.Id);
        var publicPlacePrivateChat2 = await CreateChat(false, "Public place private chat 2 two", publicPlace.Id);
        var publicPlacePublicChat1 = await CreateChat(true, "Public place public chat 1 one", publicPlace.Id);
        var publicPlacePublicChat2 = await CreateChat(true, "Public place public chat 2 two", publicPlace.Id);

        var publicPlaceMember1 = await _tester.CreateAccount("Public place Member", "One");
        var publicPlaceMember2 = await _tester.CreateAccount("Public place Member", "Two");
        var privatePlaceMember1 = await _tester.CreateAccount("Private place Member", "One");
        var privatePlaceMember2 = await _tester.CreateAccount("Private place Member", "Two");
        var everyPlaceMember1 = await _tester.CreateAccount("Every place Member", "One");
        var everyPlaceMember2 = await _tester.CreateAccount("Every place Member", "Two");
        await _tester.InviteToPlace(privatePlace.Id, privatePlaceMember1, privatePlaceMember2, everyPlaceMember1, everyPlaceMember2);
        await _tester.InviteToPlace(publicPlace.Id, publicPlaceMember1, publicPlaceMember2, everyPlaceMember1, everyPlaceMember2);

        // act
        await WaitUntilIndexed("one", 9);
        _searchUI.Text.Value = "one";
        var searchResults = await GetSearchResults(9);

        // assert
        var expected = bob.BuildSearchResults(privatePlaceMember1,
                publicPlaceMember1,
                everyPlaceMember1)
            .Concat(bob.BuildSearchResults(privateChat1,
                privatePlacePrivateChat1,
                privatePlacePublicChat1,
                publicPlacePrivateChat1,
                publicChat1,
                publicPlacePublicChat1))
            .ToArray();
        searchResults.Should().BeEquivalentTo(expected);

        // act
        _searchUI.Text.Value = "bla bla";
        searchResults = await GetSearchResults(0);

        // assert
        searchResults.Should().BeEmpty();

        // act
        _searchUI.Text.Value = "two";

        // assert
        expected = bob.BuildSearchResults(privatePlaceMember2,
                publicPlaceMember2,
                everyPlaceMember2)
            .Concat(bob.BuildSearchResults(privateChat2,
                privatePlacePrivateChat2,
                privatePlacePublicChat2,
                publicPlacePrivateChat2,
                publicChat2,
                publicPlacePublicChat2))
            .ToArray();
        await AssertSearchResults(expected);

        // act
        _chatListUI.ActivateChatList(privatePlace.Id);
        _searchUI.Text.Value = "two";

        // assert
        expected = bob.BuildSearchResults(privatePlaceMember2,
                everyPlaceMember2)
            .Concat(bob.BuildSearchResults(privatePlacePrivateChat2,
                privatePlacePublicChat2))
            .ToArray();
        await AssertSearchResults(expected);

        // act
        _chatListUI.ActivateChatList(publicPlace.Id);
        _searchUI.Text.Value = "two";

        // assert
        expected = bob.BuildSearchResults(publicPlaceMember2, everyPlaceMember2)
            .Concat(bob.BuildSearchResults(publicPlacePrivateChat2, publicPlacePublicChat2))
            .ToArray();
        await AssertSearchResults(expected);
    }

    private async Task WaitUntilIndexed(string criteria, int expectedCount)
    {
        var search = _tester.AppServices.GetRequiredService<ISearch>();
        await TestExt.When(async () => {
                var userContacts = await search.FindUserContacts(_tester.Session, null, criteria);
                var publicChatContacts = await search.FindChatContacts(_tester.Session, null, criteria, true);
                var privateChatContacts = await search.FindChatContacts(_tester.Session, null, criteria, false);
                (userContacts.Count + publicChatContacts.Count + privateChatContacts.Count).Should().BeGreaterOrEqualTo(expectedCount);
            },
            TimeSpan.FromSeconds(10));
    }

    private async Task<IReadOnlyList<ContactSearchResult>> GetSearchResults(int expectedCount)
    {
        await TestExt.When(async () => {
                var results = await _searchUI.GetContactSearchResults();
                results.Count.Should().Be(expectedCount);
            },
            TimeSpan.FromSeconds(10));
        return await _searchUI.GetContactSearchResults();
    }

    private async Task AssertSearchResults(params ContactSearchResult[] expected)
    {
        await TestExt.When(async () => {
                var results = await _searchUI.GetContactSearchResults();
                results.Should().BeEquivalentTo(expected, o => o.WithoutStrictOrdering());
            },
            TimeSpan.FromSeconds(20));
        await _searchUI.GetContactSearchResults();
    }

    private async Task<Chat> CreateChat(bool isPublic, string title, PlaceId? placeId = null)
    {
        var (id, _) = await _tester.CreateChat(isPublic, title, placeId);
        return await _tester.Chats.Get(_tester.Session, id, CancellationToken.None).Require();
    }

    private async Task<Place> CreatePlace(bool isPublic, string title)
    {
        var (id, _) = await _tester.CreatePlace(isPublic, title);
        return await _tester.Places.Get(_tester.Session, id, CancellationToken.None).Require();
    }
}
