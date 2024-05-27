using ActualChat.Contacts;
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

        // assert
        // TODO: add user contacts

        // act
        await WaitUntilIndexed(bob.Id, "one", 6);
        _searchUI.Text.Value = "one";
        var searchResults = await GetSearchResults(6);

        // assert
        searchResults.Should()
            .BeEquivalentTo(new[] {
                    privateChat1.ToSearchResult(bob.Id),
                    privatePlacePrivateChat1.ToSearchResult(bob.Id),
                    privatePlacePublicChat1.ToSearchResult(bob.Id),
                    publicPlacePrivateChat1.ToSearchResult(bob.Id),
                    publicChat1.ToSearchResult(bob.Id),
                    publicPlacePublicChat1.ToSearchResult(bob.Id),
                },
                o => o.WithStrictOrdering());

        // act
        _searchUI.Text.Value = "bla bla";
        searchResults = await GetSearchResults(0);

        // assert
        searchResults.Should().BeEmpty();

        // act
        _searchUI.Text.Value = "two";
        searchResults = await GetSearchResults(6);

        // assert
        searchResults.Should()
            .BeEquivalentTo(new[] {
                    privateChat2.ToSearchResult(bob.Id),
                    privatePlacePrivateChat2.ToSearchResult(bob.Id),
                    privatePlacePublicChat2.ToSearchResult(bob.Id),
                    publicPlacePrivateChat2.ToSearchResult(bob.Id),
                    publicChat2.ToSearchResult(bob.Id),
                    publicPlacePublicChat2.ToSearchResult(bob.Id),
                },
                o => o.WithStrictOrdering());
    }

    private async Task WaitUntilIndexed(UserId userId, string criteria, int expectedCount)
    {
        var searchBackend = _tester.AppServices.GetRequiredService<ISearchBackend>();
        await TestExt.When(async () => {
                var publicChatContacts = await searchBackend.FindContacts(userId,
                    new ContactSearchQuery {
                        Criteria = criteria,
                        Kind = ContactKind.Chat,
                        IsPublic = true,
                    },
                    CancellationToken.None);
                var privateChatContacts = await searchBackend.FindContacts(userId,
                    new ContactSearchQuery {
                        Criteria = criteria,
                        Kind = ContactKind.Chat,
                        IsPublic = false,
                    },
                    CancellationToken.None);
                (publicChatContacts.Hits.Count + privateChatContacts.Hits.Count).Should().BeGreaterOrEqualTo(expectedCount);
            },
            TimeSpan.FromSeconds(10));
    }

    private async Task<IReadOnlyList<ContactSearchResult>> GetSearchResults(int expectedCount)
    {
        await TestExt.When(async () => {
                var results = await _searchUI.GetContactSearchResults();
                results.Count.Should().Be(expectedCount);
            },
            TimeSpan.FromSeconds(20));
        return await _searchUI.GetContactSearchResults();
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
