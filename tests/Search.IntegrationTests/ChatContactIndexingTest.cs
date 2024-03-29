using ActualChat.Chat;
using ActualChat.Performance;
using ActualChat.Search.Module;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Search.IntegrationTests;

[Trait("Category", "Slow")]
public class ChatContactIndexingTest(ITestOutputHelper @out)
    : LocalAppHostTestBase( "chat_contact_indexing",
        TestAppHostOptions.Default with {
            ConfigureHost = (_, cfg) => {
                cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.IsSearchEnabled)}", "true"));
                cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.ContactIndexingDelay)}", "00:00:01"));
                cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.ContactIndexingSignalInterval)}", "00:00:00.5"));
            },
        },
        @out)
{
    private WebClientTester _tester = null!;
    private ISearchBackend _searchBackend = null!;
    private ChatContactIndexer _chatContactIndexer = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Tracer.Default = Out.NewTracer();
        _tester = AppHost.NewWebClientTester(Out);
        _searchBackend = AppHost.Services.GetRequiredService<ISearchBackend>();
        _chatContactIndexer = AppHost.Services.GetRequiredService<ChatContactIndexer>();
    }

    protected override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        await _tester.DisposeAsync().AsTask();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldIndexAll()
    {
        // arrange, act
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
        var userId = bob.Id;
        await _chatContactIndexer.WhenInitialized.WaitAsync(TimeSpan.FromSeconds(10));
        var searchResults = await Find(userId, true, "chat", 4);
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    publicChat1.ToSearchResult(bob.Id),
                    publicChat2.ToSearchResult(bob.Id),
                    publicPlacePublicChat1.ToSearchResult(bob.Id),
                    publicPlacePublicChat2.ToSearchResult(bob.Id),
                }
            );

        searchResults = await Find(userId, true, "chat 1", 2);
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    publicChat1.ToSearchResult(bob.Id),
                    publicPlacePublicChat1.ToSearchResult(bob.Id),
                }
            );

        searchResults = await Find(userId, false, "chat", 8);
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    privateChat1.ToSearchResult(bob.Id),
                    privateChat2.ToSearchResult(bob.Id),
                    publicPlacePrivateChat1.ToSearchResult(bob.Id),
                    publicPlacePrivateChat2.ToSearchResult(bob.Id),
                    privatePlacePublicChat1.ToSearchResult(bob.Id),
                    privatePlacePublicChat2.ToSearchResult(bob.Id),
                    privatePlacePrivateChat1.ToSearchResult(bob.Id),
                    privatePlacePrivateChat2.ToSearchResult(bob.Id),
                }
            );

        searchResults = await Find(userId, false, "chat 2", 4);
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    privateChat2.ToSearchResult(bob.Id),
                    publicPlacePrivateChat2.ToSearchResult(bob.Id),
                    privatePlacePublicChat2.ToSearchResult(bob.Id),
                    privatePlacePrivateChat2.ToSearchResult(bob.Id),
                }
            );
    }

    private async Task<ApiArray<ContactSearchResult>> Find(UserId userId, bool isPublic, string criteria, int expectedCount, int requestCount = 20)
    {
        ContactSearchResultPage searchResults = ContactSearchResultPage.Empty;
        await TestExt.WhenMetAsync(async () => {
                searchResults = await _searchBackend.FindChatContacts(
                    userId,
                    isPublic,
                    criteria,
                    0,
                    requestCount,
                    CancellationToken.None);
                Out.WriteLine("Found {0} out of expected {1}",
                    searchResults.Hits.Count,
                    expectedCount);
                searchResults.Offset.Should().Be(0);
                searchResults.Hits.Should().HaveCount(expectedCount);
            },
            Intervals.Exponential(TimeSpan.FromMilliseconds(100), 1.5, TimeSpan.FromMilliseconds(500)),
            TimeSpan.FromSeconds(10));
        return searchResults.Hits;
    }

    private async Task<Chat.Chat> CreateChat(bool isPublic, string title, PlaceId? placeId = null)
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
