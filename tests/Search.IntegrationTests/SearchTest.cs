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
        FluentAssertions.Formatting.Formatter.AddFormatter(new UserFormatter());
    }

    public override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        foreach (var formatter in FluentAssertions.Formatting.Formatter.Formatters.OfType<UserFormatter>().ToList())
            FluentAssertions.Formatting.Formatter.RemoveFormatter(formatter);
        await _tester.DisposeAsync().AsTask();
        _appHost.Dispose();
    }

    [Fact]
    public async Task ShouldAdd()
    {
        // arrange
        var chatId = new ChatId(Generate.Option);
        var updates = ApiArray.New(
            new IndexedEntry {
                Id = 1,
                Content = "Let's go outside",
            },
            new IndexedEntry {
                Id = 2,
                Content = "Saying something loud",
            },
            new IndexedEntry {
                Id = 3,
                Content = "Sitting on the river bank",
            },
            new IndexedEntry {
                Id = 4,
                Content = "Wake up",
            });

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
                Hits = ApiArray.New(new EntrySearchResult(new TextEntryId(chatId, 4, AssumeValid.Option),
                    SearchMatch.New("Wake up"))),
            });
    }
}
