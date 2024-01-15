using ActualChat.App.Server;
using ActualChat.Performance;
using ActualChat.Testing.Assertion;
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
        // arrange
        var chatId = new ChatId(Generate.Option);
        var updates = ApiArray.New(
            BuildEntry(1, "Let's go outside"),
            BuildEntry(2, "Saying something loud"),
            BuildEntry(3, "Sitting on the river bank"),
            BuildEntry(4, "Wake up"));

        // act
        await _commander.Call(new SearchBackend_BulkIndex(chatId, updates,  ApiArray<long>.Empty));

        // assert
        var searchResults = await _sut.Search(chatId,
            "wak",
            0,
            20,
            CancellationToken.None);
        searchResults.Should()
            .BeEquivalentTo(new SearchResultPage {
                Offset = 0,
                Hits = ApiArray.New(BuildSearchResult(chatId, 4, "Wake up")),
            });
    }

    [Fact]
    public async Task ShouldFindIfUpdatedMatchesCriteria()
    {
        // arrange
        var chatId = new ChatId(Generate.Option);
        var updates = ApiArray.New(
            BuildEntry(1, "Let's go outside"),
            BuildEntry(2, "Saying something loud"),
            BuildEntry(3, "Sitting on the river bank"),
            BuildEntry(4, "Wake up"));
        await _commander.Call(new SearchBackend_BulkIndex(chatId, updates,  ApiArray<long>.Empty));

        // act
        updates = ApiArray.New(BuildEntry(3, "Waking up..."));
        await _commander.Call(new SearchBackend_BulkIndex(chatId, updates,  ApiArray<long>.Empty));

        // assert
        var searchResults = await _sut.Search(chatId,
            "wak",
            0,
            20,
            CancellationToken.None);
        searchResults.Should()
            .BeEquivalentTo(new SearchResultPage {
                Offset = 0,
                Hits = ApiArray.New(BuildSearchResult(chatId, 3, "Waking up..."),
                    BuildSearchResult(chatId, 4, "Wake up")),
            });
    }

    [Fact]
    public async Task ShouldNotFindDeleted()
    {
        // arrange
        var chatId = new ChatId(Generate.Option);
        var updates = ApiArray.New(
            BuildEntry(1, "Let's go outside"),
            BuildEntry(2, "Saying something loud"),
            BuildEntry(3, "Sitting on the river bank"),
            BuildEntry(4, "Wake up"));
        await _commander.Call(new SearchBackend_BulkIndex(chatId, updates,  ApiArray<long>.Empty));

        // act
        updates = ApiArray.New(BuildEntry(3, "Waking up..."));
        var removes = ApiArray.New(4L);
        await _commander.Call(new SearchBackend_BulkIndex(chatId, updates,  removes));

        // assert
        var searchResults = await _sut.Search(chatId,
            "wak",
            0,
            20,
            CancellationToken.None);
        searchResults.Should()
            .BeEquivalentTo(new SearchResultPage {
                Offset = 0,
                Hits = ApiArray.New(BuildSearchResult(chatId, 3, "Waking up...")),
            });
    }

    private static IndexedEntry BuildEntry(long lid, string content)
        => new() {
            Id = lid,
            Content = content,
        };

    private static EntrySearchResult BuildSearchResult(ChatId chatId, long lid, string content)
        => new (new TextEntryId(chatId, lid, AssumeValid.Option), SearchMatch.New(content));
}
