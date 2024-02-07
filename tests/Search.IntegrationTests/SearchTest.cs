using ActualChat.App.Server;
using ActualChat.Chat;
using ActualChat.Performance;
using ActualChat.Testing.Host;

namespace ActualChat.Search.IntegrationTests;

public class SearchTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    private WebClientTester _tester = null!;
    private ISearchBackend _sut = null!;
    private AppHost _appHost = null!;
    private ICommander _commander = null!;

    public override async Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        _appHost = await NewAppHost();
        _tester = _appHost.NewWebClientTester();
        _sut = _appHost.Services.GetRequiredService<ISearchBackend>();
        _commander = _appHost.Services.Commander();
    }

    public override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        await _tester.DisposeAsync().AsTask();
        _appHost.Dispose();
    }

    [Fact]
    public async Task ShouldAdd()
    {
        if (TestRunnerInfo.IsBuildAgent())
            return; // The test sometimes times out on GitHub

        // arrange
        var bob = await _tester.SignInAsBob();
        var chat = await CreateChat(bob.Id);

        var updates = ApiArray.New(
            BuildEntry(chat.Id, 1, "Let's go outside"),
            BuildEntry(chat.Id, 2, "Saying something loud"),
            BuildEntry(chat.Id, 3, "Sitting on the river bank"),
            BuildEntry(chat.Id, 4, "Wake up"));

        // act
        await _commander.Call(new SearchBackend_BulkIndex(chat.Id, updates,  ApiArray<long>.Empty));
        await _commander.Call(new SearchBackend_Refresh(chat.Id));

        // assert
        var searchResults = await _sut.SearchInChat(chat.Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        searchResults.Should()
            .BeEquivalentTo(new SearchResultPage {
                Offset = 0,
                Hits = ApiArray.New(BuildSearchResult(chat.Id, 4, "Wake up")),
            });
    }

    [Fact]
    public async Task ShouldAddForPlace()
    {
        if (TestRunnerInfo.IsBuildAgent())
            return; // The test sometimes times out on GitHub

        // arrange
        var bob = await _tester.SignInAsBob();
        var place = await CreateChat(bob.Id, ChatKind.Place, "Bob's Place");
        var publicChat1 = await CreateChat(bob.Id, ChatKind.Group, "Public Group Chat 1", place.Id.PlaceId);
        var publicChat2 = await CreateChat(bob.Id, ChatKind.Group, "Public Group Chat 1", place.Id.PlaceId);
        var privateChat1 = await CreateChat(bob.Id, ChatKind.Group, "Private Group Chat 1", place.Id.PlaceId);
        var privateChat2 = await CreateChat(bob.Id, ChatKind.Group, "Private Group Chat 1", place.Id.PlaceId);

        var publicChat1Updates = ApiArray.New(
            BuildEntry(publicChat1.Id, 1, "PublicChat1: Let's go outside"),
            BuildEntry(publicChat1.Id, 2, "PublicChat1: Saying something loud"),
            BuildEntry(publicChat1.Id, 3, "PublicChat1: Sitting on the river bank"),
            BuildEntry(publicChat1.Id, 4, "PublicChat1: Wake up"));

        var publicChat2Updates = ApiArray.New(
            BuildEntry(publicChat2.Id, 1, "PublicChat2: Let's go outside"),
            BuildEntry(publicChat2.Id, 2, "PublicChat2: Saying something loud"),
            BuildEntry(publicChat2.Id, 3, "PublicChat2: Sitting on the river bank"),
            BuildEntry(publicChat2.Id, 4, "PublicChat2: Wake up"));

        var privateChat1Updates = ApiArray.New(
            BuildEntry(privateChat1.Id, 1, "PrivateChat1: Let's go outside"),
            BuildEntry(privateChat1.Id, 2, "PrivateChat1: Saying something loud"),
            BuildEntry(privateChat1.Id, 3, "PrivateChat1: Sitting on the river bank"),
            BuildEntry(privateChat1.Id, 4, "PrivateChat1: Wake up"));

        var privateChat2Updates = ApiArray.New(
            BuildEntry(privateChat2.Id, 1, "PrivateChat2: Let's go outside"),
            BuildEntry(privateChat2.Id, 2, "PrivateChat2: Saying something loud"),
            BuildEntry(privateChat2.Id, 3, "PrivateChat2: Sitting on the river bank"),
            BuildEntry(privateChat2.Id, 4, "PrivateChat2: Wake up"));

        // act
        await _commander.Call(new SearchBackend_BulkIndex(publicChat1.Id, publicChat1Updates,  ApiArray<long>.Empty));
        await _commander.Call(new SearchBackend_BulkIndex(publicChat2.Id, publicChat2Updates,  ApiArray<long>.Empty));
        await _commander.Call(new SearchBackend_BulkIndex(privateChat1.Id, privateChat1Updates,  ApiArray<long>.Empty));
        await _commander.Call(new SearchBackend_BulkIndex(privateChat2.Id, privateChat2Updates,  ApiArray<long>.Empty));
        await _commander.Call(new SearchBackend_Refresh(publicChat1.Id, publicChat2.Id, privateChat1.Id, privateChat2.Id));

        // assert
        var publicChat1SearchResults = await _sut.SearchInChat(publicChat1.Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        publicChat1SearchResults.Should()
            .BeEquivalentTo(new SearchResultPage {
                Offset = 0,
                Hits = ApiArray.New(BuildSearchResult(publicChat1.Id, 4, "PublicChat1: Wake up")),
            });
        var publicChat2SearchResults = await _sut.SearchInChat(publicChat2.Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        publicChat2SearchResults.Should()
            .BeEquivalentTo(new SearchResultPage {
                Offset = 0,
                Hits = ApiArray.New(BuildSearchResult(publicChat2.Id, 4, "PublicChat2: Wake up")),
            });
        var privateChat1SearchResults = await _sut.SearchInChat(privateChat1.Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        privateChat1SearchResults.Should()
            .BeEquivalentTo(new SearchResultPage {
                Offset = 0,
                Hits = ApiArray.New(BuildSearchResult(privateChat1.Id, 4, "PrivateChat1: Wake up")),
            });
        var privateChat2SearchResults = await _sut.SearchInChat(privateChat2.Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        privateChat2SearchResults.Should()
            .BeEquivalentTo(new SearchResultPage {
                Offset = 0,
                Hits = ApiArray.New(BuildSearchResult(privateChat2.Id, 4, "PrivateChat2: Wake up")),
            });
        var globalSearchResults = await _sut.SearchInAllChats(bob.Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        globalSearchResults.Should()
            .BeEquivalentTo(new SearchResultPage {
                Offset = 0,
                Hits = ApiArray.New(
                    BuildSearchResult(publicChat1.Id, 4, "PublicChat1: Wake up"),
                    BuildSearchResult(publicChat2.Id, 4, "PublicChat2: Wake up"),
                    BuildSearchResult(privateChat1.Id, 4, "PrivateChat1: Wake up"),
                    BuildSearchResult(privateChat2.Id, 4, "PrivateChat2: Wake up")),
            });
    }

    [Fact]
    public async Task ShouldFindIfUpdatedMatchesCriteria()
    {
        if (TestRunnerInfo.IsBuildAgent())
            return; // The test sometimes times out on GitHub

        // arrange
        var bob = await _tester.SignInAsBob();
        var chat = await CreateChat(bob.Id);
        var updates = ApiArray.New(
            BuildEntry(chat.Id, 1, "Let's go outside"),
            BuildEntry(chat.Id, 2, "Saying something loud"),
            BuildEntry(chat.Id, 3, "Sitting on the river bank"),
            BuildEntry(chat.Id, 4, "Wake up"));
        await _commander.Call(new SearchBackend_BulkIndex(chat.Id, updates,  ApiArray<long>.Empty));
        await _commander.Call(new SearchBackend_Refresh(chat.Id));

        // act
        updates = ApiArray.New(BuildEntry(chat.Id, 3, "Waking up..."));
        await _commander.Call(new SearchBackend_BulkIndex(chat.Id, updates,  ApiArray<long>.Empty));
        await _commander.Call(new SearchBackend_Refresh(chat.Id));

        // assert
        var searchResults = await _sut.SearchInChat(chat.Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        searchResults.Should()
            .BeEquivalentTo(new SearchResultPage {
                Offset = 0,
                Hits = ApiArray.New(BuildSearchResult(chat.Id, 3, "Waking up..."),
                    BuildSearchResult(chat.Id, 4, "Wake up")),
            });
    }

    [Fact]
    public async Task ShouldNotFindDeleted()
    {
        // arrange
        var bob = await _tester.SignInAsBob();
        var chat = await CreateChat(bob.Id);
        var updates = ApiArray.New(
            BuildEntry(chat.Id, 1, "Let's go outside"),
            BuildEntry(chat.Id, 2, "Saying something loud"),
            BuildEntry(chat.Id, 3, "Sitting on the river bank"),
            BuildEntry(chat.Id, 4, "Wake up"));
        await _commander.Call(new SearchBackend_BulkIndex(chat.Id, updates,  ApiArray<long>.Empty));
        await _commander.Call(new SearchBackend_Refresh(chat.Id));

        // act
        updates = ApiArray.New(BuildEntry(chat.Id, 3, "Waking up..."));
        var removes = ApiArray.New(4L);
        await _commander.Call(new SearchBackend_BulkIndex(chat.Id, updates,  removes));
        await _commander.Call(new SearchBackend_Refresh(chat.Id));

        // assert
        var searchResults = await _sut.SearchInChat(chat.Id,
            "wak",
            0,
            20,
            CancellationToken.None);
        searchResults.Should()
            .BeEquivalentTo(new SearchResultPage {
                Offset = 0,
                Hits = ApiArray.New(BuildSearchResult(chat.Id, 3, "Waking up...")),
            });
    }

    private static IndexedEntry BuildEntry(ChatId chatId, long lid, string content)
        => new() {
            Id = new TextEntryId(chatId, lid, AssumeValid.Option),
            Content = content,
            ChatId = chatId,
        };

    private static EntrySearchResult BuildSearchResult(ChatId chatId, long lid, string content)
        => new (new TextEntryId(chatId, lid, AssumeValid.Option), SearchMatch.New(content));

    private Task<Chat.Chat> CreateChat(UserId ownerId, ChatKind kind = ChatKind.Group, string title = "some chat", PlaceId placeId = default)
    {
        var cmd = new ChatsBackend_Change(default,
            0,
            Change.Create(new ChatDiff {
                Kind = kind,
                Title = title,
                PlaceId = placeId,
            }),
            ownerId);
        return _commander.Call(cmd);
    }
}
