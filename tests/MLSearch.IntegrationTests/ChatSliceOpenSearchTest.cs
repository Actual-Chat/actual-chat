using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch;
using ActualChat.Performance;
using ActualChat.Testing.Host;
using OpenSearch.Client;
using OpenSearch.Net;
using HttpMethod = OpenSearch.Net.HttpMethod;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
public class ChatSliceOpenSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    protected override async Task InitializeAsync()
        => await base.InitializeAsync();

    protected override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        var settings = AppHost.Services.GetRequiredService<IIndexSettingsSource>().GetSettings<ChatSlice>();
        var searchIndexName = settings.SearchIndexName;
        var cursorIndexName = settings.CursorIndexName;
        var pipelineId = settings.IngestPipelineId;

        var client = AppHost.Services.GetRequiredService<IOpenSearchClient>();
        await client.LowLevel.DoRequestAsync<StringResponse>(HttpMethod.DELETE, $"/{searchIndexName}", CancellationToken.None);
        await client.LowLevel.DoRequestAsync<StringResponse>(HttpMethod.DELETE, $"/{cursorIndexName}", CancellationToken.None);
        await client.LowLevel.DoRequestAsync<StringResponse>(HttpMethod.DELETE, $"/_ingest/pipeline/{pipelineId}", CancellationToken.None);

        await base.DisposeAsync();
    }

    [Fact]
    public async Task SemanticSearchTest()
    {
        var authorId = new PrincipalId(UserId.New(), AssumeValid.Option);
        var chatId1 = new ChatId(Generate.Option);
        var entryIds1 = Enumerable.Range(1, 5).Select(id => new ChatEntryId(chatId1, ChatEntryKind.Text, id, AssumeValid.Option));
        var textItems1 = new [] {
            "OpenSearch supports the following models, categorized by type.",
            "Quite often, our methods are async, and we can't make constructors async. This is where XUnit's IAsyncLifetime comes in.",
            "The license for Docker Desktop is not expensive, but it's not free anymore, so I wanted to check if there's an easy and cheap alternative to Docker Desktop for Windows.",
            "The sdkmanager is a command-line tool that lets you view, install, update, and uninstall packages for the Android SDK.",
            "In Barcelona, a migrant squatter was asked to leave by the property owner. He refused and threatened the home owner with a hammer.",
        };
        var chatId2 = new ChatId(Generate.Option);
        var entryIds2 = Enumerable.Range(1, 5).Select(id => new ChatEntryId(chatId2, ChatEntryKind.Text, id, AssumeValid.Option));
        var textItems2 = new [] {
            "Language clients are forward compatible; meaning that clients support communicating with greater or equal minor versions of Elasticsearch.",
            "When it comes to your electricity plan, there’s always room for improvement. Switch to Cirro and make your life easier.",
            "I take my role as leader of the city with the largest Jewish community anywhere outside of Israel very seriously.",
            "Build your apps faster with world-class developer tools that help you write precise, accurate, and maintainable code the first time.",
            "SURFY is a free search engine that gives 80% of ad revenue towards ocean plastic cleanup. We partnered with the most popular search engines like Google , Bing and Amazon Smile.",
        };
        var documents = entryIds1.Zip(textItems1)
            .Zip(entryIds2.Zip(textItems2))
            .SelectMany(args => new [] {args.First, args.Second})
            .Select((args, i) => {
                var (id, text) = args;
                var metadata = new ChatSliceMetadata(
                    authorId,
                    [id], null, null,
                    [], [], [], [], [],
                    false,
                    "en-US",
                    DateTime.Now.AddDays(-(i/2))
                );
                return new ChatSlice(metadata, text);
            });

        var searchEngine = AppHost.Services.GetRequiredService<ISearchEngine<ChatSlice>>();

        foreach (var document in documents) {
            await searchEngine.Ingest(document, CancellationToken.None);
        }

        await Task.Delay(200);

        var query1 = new SearchQuery() {
            Keywords=["command"],
            FreeTextFilter="Tools for mobile development",
        };
        var queryResult1 = await searchEngine.Find(query1, CancellationToken.None);
        Assert.True(queryResult1.Documents.Count > 0);

        var metadataField = JsonNamingPolicy.CamelCase.ConvertName(nameof(ChatSlice.Metadata));
        var timestampField = JsonNamingPolicy.CamelCase.ConvertName(nameof(ChatSliceMetadata.Timestamp));
        var chatIdField = JsonNamingPolicy.CamelCase.ConvertName(nameof(ChatSliceMetadata.ChatId));
        var dateBound = DateTime.Now.AddDays(-3);
        var query2 = new SearchQuery() {
            MetadataFilters=[
                new DateRangeFilter($"{metadataField}.{timestampField}", new RangeBound<DateTime>(dateBound, true), null),
            ],
            FreeTextFilter="Search engines and technologies",
        };

        var queryResult2 = await searchEngine.Find(query2, CancellationToken.None);
        var query2Count = queryResult2.Documents.Count;
        Assert.True(query2Count > 0);

        var query3 = new SearchQuery() {
            MetadataFilters=[
                new DateRangeFilter($"{metadataField}.{timestampField}", new RangeBound<DateTime>(dateBound, true), null),
                new EqualityFilter<ChatId>($"{metadataField}.{chatIdField}", chatId1),
            ],
            FreeTextFilter="Search engines and technologies",
        };
        var queryResult3 = await searchEngine.Find(query3, CancellationToken.None);
        var query3Count = queryResult3.Documents.Count;
        Assert.True(query3Count > 0);
        Assert.True(query2Count > query3Count);
    }
}
