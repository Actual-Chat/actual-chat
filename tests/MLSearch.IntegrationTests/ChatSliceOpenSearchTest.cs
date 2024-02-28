using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.SearchEngine.OpenSearch;
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
    private Uri OpenSearchClusterUri => new("http://localhost:9201");
    private string OpenSearchModelGroupName => "NLP_model_group";
    private OpenSearchClient? client;
    private OpenSearchClusterSettings? settings;

    protected override async Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        var config = new ConnectionSettings(OpenSearchClusterUri)
            .PrettyJson()
            .DefaultFieldNameInferrer(f => f);
        client = new OpenSearchClient(config);
        var setup = new OpenSearchClusterSetup(
            OpenSearchModelGroupName,
            client,
            null,
            null
        );
        await setup.Initialize(default);
        settings = setup.Result;
        await base.InitializeAsync();
    }

    protected override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        var searchIndexId = settings!.IntoSearchIndexId();
        var pipelineName = settings.IntoIngestPipelineId();

        await client!.LowLevel.DoRequestAsync<StringResponse>(HttpMethod.DELETE, $"/{searchIndexId}", CancellationToken.None);
        await client!.LowLevel.DoRequestAsync<StringResponse>(HttpMethod.DELETE, $"/_ingest/pipeline/{pipelineName}", CancellationToken.None);

        await base.DisposeAsync();
    }

    [Fact]
    public async Task SemanticSearchTest()
    {
        var searchIndexId = settings!.IntoSearchIndexId();
        var authorId = new PrincipalId(UserId.New(), AssumeValid.Option);
        var chatId = new ChatId(Generate.Option);
        var entryIds = Enumerable.Range(1, 5).Select(id => new ChatEntryId(chatId, ChatEntryKind.Text, id, AssumeValid.Option));
        var textItems = new [] {
            "OpenSearch supports the following models, categorized by type.",
            "Quite often, our methods are async, and we can't make constructors async. This is where XUnit's IAsyncLifetime comes in.",
            "The license for Docker Desktop is not expensive, but it's not free anymore, so I wanted to check if there's an easy and cheap alternative to Docker Desktop for Windows.",
            "The sdkmanager is a command-line tool that lets you view, install, update, and uninstall packages for the Android SDK.",
            "In Barcelona, a migrant squatter was asked to leave by the property owner. He refused and threatened the home owner with a hammer.",
        };
        var documents = entryIds.Zip(textItems).Select(args => {
            var (id, text) = args;
            var metadata = new ChatSliceMetadata(
                authorId,
                [id], null, null,
                [], [], [], [], [],
                false,
                "en-US",
                DateTime.Now
            );
            return new ChatSlice(metadata, text);
        });
        foreach (var document in documents) {
            var newDocResponse = await client!.LowLevel.DoRequestAsync<StringResponse>(
                HttpMethod.PUT, $"/{searchIndexId}/_doc/{document.Id}", CancellationToken.None, PostData.Serializable(document));
            Assert.True(newDocResponse.Success);
        }

        var query = new VectorSearchQuery() { FreeTextFilter="Tools for mobile development", Keywords=["command"] };
        var searchEngine = new OpenSearchEngine(client, settings, NullLoggerSource.Instance);
        var queryResult = await searchEngine.Find(query, CancellationToken.None);
        Assert.True(queryResult.Documents.Count > 0);
    }
}
