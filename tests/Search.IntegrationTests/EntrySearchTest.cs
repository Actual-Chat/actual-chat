using ActualChat.App.Server;
using ActualChat.Performance;
using ActualChat.Testing.Host;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection)), Trait("Category", nameof(SearchCollection))]
public class EntrySearchTest(AppHostFixture fixture, ITestOutputHelper @out): IAsyncLifetime
{
    private TestAppHost Host => fixture.Host;
    private ITestOutputHelper Out { get; } = fixture.Host.UseOutput(@out);

    private WebClientTester _tester = null!;
    private ISearchBackend _sut = null!;
    private ICommander _commander = null!;

    public Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        _tester = Host.NewWebClientTester(Out);
        _sut = Host.Services.GetRequiredService<ISearchBackend>();
        _commander = Host.Services.Commander();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        await _tester.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task ShouldAdd()
    {
        // arrange
        await _tester.SignInAsBob();
        var chatId = await CreateChat();

        var updates = ApiArray.New(
            BuildEntry(chatId, 1, "Let's go outside"),
            BuildEntry(chatId, 2, "Saying something loud"),
            BuildEntry(chatId, 3, "Sitting on the river bank"),
            BuildEntry(chatId, 4, "Wake up"));

        // act
        await _commander.Call(new SearchBackend_EntryBulkIndex(chatId, updates,  ApiArray<IndexedEntry>.Empty));
        await _commander.Call(new SearchBackend_Refresh(chatId));

        // assert
        var searchResults = await _sut.FindEntriesInChat(chatId,
            "wak",
            0,
            20,
            CancellationToken.None);
        searchResults.Should().BeEquivalentTo(BuildResponse(BuildSearchResult(chatId, 4, "Wake up")));
    }

    [Fact]
    public async Task ShouldAddForPlace()
    {
        // arrange
        var bob = await _tester.SignInAsBob();
        var placeId = await CreatePlace("Bob's Place");
        var publicChat1Id = await CreateChat(ChatKind.Group, "Public Group Chat 1", placeId);
        var publicChat2Id = await CreateChat(ChatKind.Group, "Public Group Chat 1", placeId);
        var privateChat1Id = await CreateChat(ChatKind.Group, "Private Group Chat 1", placeId);
        var privateChat2Id = await CreateChat(ChatKind.Group, "Private Group Chat 1", placeId);

        var publicChat1Updates = ApiArray.New(
            BuildEntry(publicChat1Id, 1, "PublicChat1: Let's go outside"),
            BuildEntry(publicChat1Id, 2, "PublicChat1: Saying something loud"),
            BuildEntry(publicChat1Id, 3, "PublicChat1: Sitting on the river bank"),
            BuildEntry(publicChat1Id, 4, "PublicChat1: Wake up"));

        var publicChat2Updates = ApiArray.New(
            BuildEntry(publicChat2Id, 1, "PublicChat2: Let's go outside"),
            BuildEntry(publicChat2Id, 2, "PublicChat2: Saying something loud"),
            BuildEntry(publicChat2Id, 3, "PublicChat2: Sitting on the river bank"),
            BuildEntry(publicChat2Id, 4, "PublicChat2: Wake up"));

        var privateChat1Updates = ApiArray.New(
            BuildEntry(privateChat1Id, 1, "PrivateChat1: Let's go outside"),
            BuildEntry(privateChat1Id, 2, "PrivateChat1: Saying something loud"),
            BuildEntry(privateChat1Id, 3, "PrivateChat1: Sitting on the river bank"),
            BuildEntry(privateChat1Id, 4, "PrivateChat1: Wake up"));

        var privateChat2Updates = ApiArray.New(
            BuildEntry(privateChat2Id, 1, "PrivateChat2: Let's go outside"),
            BuildEntry(privateChat2Id, 2, "PrivateChat2: Saying something loud"),
            BuildEntry(privateChat2Id, 3, "PrivateChat2: Sitting on the river bank"),
            BuildEntry(privateChat2Id, 4, "PrivateChat2: Wake up"));

        // act
        await _commander.Call(new SearchBackend_EntryBulkIndex(publicChat1Id, publicChat1Updates,  ApiArray<IndexedEntry>.Empty));
        await _commander.Call(new SearchBackend_EntryBulkIndex(publicChat2Id, publicChat2Updates,  ApiArray<IndexedEntry>.Empty));
        await _commander.Call(new SearchBackend_EntryBulkIndex(privateChat1Id, privateChat1Updates,  ApiArray<IndexedEntry>.Empty));
        await _commander.Call(new SearchBackend_EntryBulkIndex(privateChat2Id, privateChat2Updates,  ApiArray<IndexedEntry>.Empty));
        await _commander.Call(new SearchBackend_Refresh(publicChat1Id, publicChat2Id, privateChat1Id, privateChat2Id));

        // assert
        var publicChat1SearchResults = await _sut.FindEntriesInChat(publicChat1Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        publicChat1SearchResults.Should()
            .BeEquivalentTo(BuildResponse(BuildSearchResult(publicChat1Id, 4, "PublicChat1: Wake up")));
        var publicChat2SearchResults = await _sut.FindEntriesInChat(publicChat2Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        publicChat2SearchResults.Should()
            .BeEquivalentTo(BuildResponse(BuildSearchResult(publicChat2Id, 4, "PublicChat2: Wake up")));
        var privateChat1SearchResults = await _sut.FindEntriesInChat(privateChat1Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        privateChat1SearchResults.Should()
            .BeEquivalentTo(BuildResponse(BuildSearchResult(privateChat1Id, 4, "PrivateChat1: Wake up")));
        var privateChat2SearchResults = await _sut.FindEntriesInChat(privateChat2Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        privateChat2SearchResults.Should()
            .BeEquivalentTo(BuildResponse(BuildSearchResult(privateChat2Id, 4, "PrivateChat2: Wake up")));
        var globalSearchResults = await _sut.FindEntriesInAllChats(bob.Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        globalSearchResults.Should()
            .BeEquivalentTo(BuildResponse(
                    BuildSearchResult(publicChat1Id, 4, "PublicChat1: Wake up"),
                    BuildSearchResult(publicChat2Id, 4, "PublicChat2: Wake up"),
                    BuildSearchResult(privateChat1Id, 4, "PrivateChat1: Wake up"),
                    BuildSearchResult(privateChat2Id, 4, "PrivateChat2: Wake up")));
    }

    [Fact]
    public async Task ShouldFindIfUpdatedMatchesCriteria()
    {
        // arrange
        await _tester.SignInAsBob();
        var chatId = await CreateChat();
        var updates = ApiArray.New(
            BuildEntry(chatId, 1, "Let's go outside"),
            BuildEntry(chatId, 2, "Saying something loud"),
            BuildEntry(chatId, 3, "Sitting on the river bank"),
            BuildEntry(chatId, 4, "Wake up"));
        await _commander.Call(new SearchBackend_EntryBulkIndex(chatId, updates,  ApiArray<IndexedEntry>.Empty));
        await _commander.Call(new SearchBackend_Refresh(chatId));

        // act
        updates = ApiArray.New(BuildEntry(chatId, 3, "Waking up..."));
        await _commander.Call(new SearchBackend_EntryBulkIndex(chatId, updates,  ApiArray<IndexedEntry>.Empty));
        await _commander.Call(new SearchBackend_Refresh(chatId));

        // assert
        var searchResults = await _sut.FindEntriesInChat(chatId,
            "wak",
            0,
            20,
            CancellationToken.None);
        searchResults.Should()
            .BeEquivalentTo(BuildResponse(BuildSearchResult(chatId, 3, "Waking up..."),
                BuildSearchResult(chatId, 4, "Wake up")));
    }

    [Fact]
    public async Task ShouldNotFindDeleted()
    {
        // arrange
        await _tester.SignInAsBob();
        var chatId = await CreateChat();
        var updates = ApiArray.New(
            BuildEntry(chatId, 1, "Let's go outside"),
            BuildEntry(chatId, 2, "Saying something loud"),
            BuildEntry(chatId, 3, "Sitting on the river bank"),
            BuildEntry(chatId, 4, "Wake up"));
        await _commander.Call(new SearchBackend_EntryBulkIndex(chatId, updates,  ApiArray<IndexedEntry>.Empty));
        await _commander.Call(new SearchBackend_Refresh(chatId));

        // act
        updates = ApiArray.New(BuildEntry(chatId, 3, "Waking up..."));
        var removes = ApiArray.New(BuildEntry(chatId, 4L, ""));
        await _commander.Call(new SearchBackend_EntryBulkIndex(chatId, updates,  removes));
        await _commander.Call(new SearchBackend_Refresh(chatId));

        // assert
        var searchResults = await _sut.FindEntriesInChat(chatId,
            "wak",
            0,
            20,
            CancellationToken.None);
        searchResults.Should()
            .BeEquivalentTo(BuildResponse(BuildSearchResult(chatId, 3, "Waking up...")));
    }

    private static IndexedEntry BuildEntry(ChatId chatId, long lid, string content)
        => new() {
            Id = new TextEntryId(chatId, lid, AssumeValid.Option),
            Content = content,
            ChatId = chatId,
        };

    private static EntrySearchResult BuildSearchResult(ChatId chatId, long lid, string content)
        => new (new TextEntryId(chatId, lid, AssumeValid.Option), SearchMatch.New(content));

    private static EntrySearchResultPage BuildResponse(params EntrySearchResult[] hits)
        => new() {
            Offset = 0,
            Hits = hits.ToApiArray(),
        };

    private async Task<ChatId> CreateChat(ChatKind kind = ChatKind.Group, string title = "some chat", PlaceId placeId = default)
    {
        var (id, _) = await _tester.CreateChat(x => x with {
            Kind = kind,
            Title = title,
            PlaceId = placeId,
        });
        return id;
    }

    private async Task<PlaceId> CreatePlace(string title = "some place")
    {
        var (id, _) = await _tester.CreatePlace(x => x with {
            Title = title,
        });
        return id;
    }
}
