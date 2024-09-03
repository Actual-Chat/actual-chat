using System.Text;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.Testing.Host;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.MLSearch.IntegrationTests.OpenSearch;

[Trait("Category", "Slow")]
[Collection(nameof(MLSearchCollection))]
public class ChatContentSemanticSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // Cleanup all test indexes before each test method start
        var client = AppHost.Services.GetRequiredService<IOpenSearchClient>();
        var deleteByQueryResponse = await client.DeleteByQueryAsync<object>(d => d
            .Index(OpenSearchNames.MLTestIndexPattern)
            .Refresh(true)
            .WaitForCompletion(true)
            .Query(query => query.Script(
                scriptQuery => scriptQuery.Script(
                    script => script.Source("true")
                ))
            )
        );
        deleteByQueryResponse.AssertSuccess();
    }

    [Fact]
    public async Task SemanticSearchTest()
    {
        var authorId = new PrincipalId(UserId.New(), AssumeValid.Option);
        var chatId1 = new ChatId(Generate.Option);
        var chatInfo1 = new ChatInfo(chatId1, true, false);
        var entryIds1 = Enumerable.Range(1, 5)
            .Select(id => new ChatEntryId(chatId1, ChatEntryKind.Text, id, AssumeValid.Option))
            .ToArray();
        var textItems1 = new [] {
            "OpenSearch supports the following models, categorized by type.",
            "Quite often, our methods are async, and we can't make constructors async. This is where XUnit's IAsyncLifetime comes in.",
            "The license for Docker Desktop is not expensive, but it's not free anymore, so I wanted to check if there's an easy and cheap alternative to Docker Desktop for Windows.",
            "The sdkmanager is a command-line tool that lets you view, install, update, and uninstall packages for the Android SDK.",
            "In Barcelona, a migrant squatter was asked to leave by the property owner. He refused and threatened the home owner with a hammer.",
        };
        var chatId2 = new ChatId(Generate.Option);
        var chatInfo2 = new ChatInfo(chatId2, true, false);
        var entryIds2 = Enumerable.Range(1, 5)
            .Select(id => new ChatEntryId(chatId2, ChatEntryKind.Text, id, AssumeValid.Option))
            .ToArray();
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
                    [authorId],
                    [new ChatSliceEntry(id, 1, 1)], null, null,
                    [], [], [], [],
                    "en-US",
                    DateTime.Now.AddDays(-(i/2))
                );
                return new ChatSlice(metadata, text);
            });

        // Ingest documents to the index
        var chatInfoSink = AppHost.Services.GetRequiredService<ISink<ChatInfo, string>>();
        await chatInfoSink.ExecuteAsync([chatInfo1, chatInfo2], null);

        var documentSink = AppHost.Services.GetRequiredService<ISink<ChatSlice, string>>();
        await documentSink.ExecuteAsync(documents.ToArray(), null);

        // Ensure all documents processed by the ingestion pipeline
        var documentLoader = AppHost.Services.GetRequiredService<IChatContentDocumentLoader>();
        const int maxAttempt = 20;
        foreach (var attempt in Enumerable.Range(0, maxAttempt + 1)) {
            if (attempt==maxAttempt) {
                Assert.Fail("Failure to confirm documents are in the OpenSearch index.");
            }
            var indexDocs = await documentLoader.LoadByEntryIdsAsync(entryIds1.Concat(entryIds2));
            if (indexDocs.Count == entryIds1.Length + entryIds2.Length) {
                break;
            }
            await Task.Delay(50);
        }

        var searchEngine = AppHost.Services.GetRequiredService<ISearchEngine<ChatSlice>>();

        var query1 = new SearchQuery() {
            Filters = [
                new KeywordFilter<ChatSlice>(["command"]),
                new SemanticFilter<ChatSlice>("Tools for mobile development"),
            ],
        };
        var queryResult1 = await searchEngine.Find(query1, CancellationToken.None);
        Assert.True(queryResult1.Documents.Count > 0);

        var namingPolicy = AppHost.Services.GetRequiredService<OpenSearchNamingPolicy>();
        var metadataField = namingPolicy.ConvertName(nameof(ChatSlice.Metadata));
        var timestampField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.ContentTimestamp));
        var chatIdField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.ChatId));
        var dateBound = DateTime.Now.AddDays(-3);
        var query2 = new SearchQuery() {
            Filters = [
                new DateRangeFilter($"{metadataField}.{timestampField}", new RangeBound<DateTime>(dateBound, true), null),
                new SemanticFilter<ChatSlice>("Search engines and technologies"),
            ],
        };

        var queryResult2 = await searchEngine.Find(query2, CancellationToken.None);
        var query2Count = queryResult2.Documents.Count;
        Assert.True(query2Count > 0);

        var query3 = new SearchQuery() {
            Filters = [
                new DateRangeFilter($"{metadataField}.{timestampField}", new RangeBound<DateTime>(dateBound, true), null),
                new EqualityFilter<ChatId>($"{metadataField}.{chatIdField}", chatId1),
                new SemanticFilter<ChatSlice>("Search engines and technologies"),
            ],
        };
        var queryResult3 = await searchEngine.Find(query3, CancellationToken.None);
        var query3Count = queryResult3.Documents.Count;
        Assert.True(query3Count > 0);
        Assert.True(query2Count > query3Count);
    }

    [Fact]
    public void ResolvesOfIndexingServicesWorkCorrectly()
    {
        Assert.NotNull(AppHost.Services.GetService<IChatContentUpdateLoader>());
        Assert.NotNull(AppHost.Services.GetService<ICursorStates<ChatContentCursor>>());

        Assert.NotNull(AppHost.Services.GetService<IChatIndexTrigger>());
        Assert.NotNull(AppHost.Services.GetService<IChatContentDocumentLoader>());
        Assert.NotNull(AppHost.Services.GetService<IChatContentMapper>());

        Assert.NotNull(AppHost.Services.GetService<ISink<ChatSlice, string>>());
        Assert.NotNull(AppHost.Services.GetService<IChatContentIndexerFactory>());

        Assert.NotNull(AppHost.Services.GetService<IChatContentIndexWorker>());
    }

    [Fact]
    public void ChatSliceSerializesAndDeserializesProperly()
    {
        var client = AppHost.Services.GetRequiredService<IOpenSearchClient>();

        const int localEntryId = 111;
        var authorId = new PrincipalId(UserId.New(), AssumeValid.Option);
        var chatId = new ChatId(Generate.Option);
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, localEntryId, AssumeValid.Option);

        var metadata = new ChatSliceMetadata(
            [authorId],
            [new ChatSliceEntry(chatEntryId, localEntryId, 1)], null, null,
            [], [], [], [],
            "en-US",
            DateTime.Now
        );
        var text = "Serialize Me";
        var document = new ChatSlice(metadata, text);

        var serializer = client.SourceSerializer;
        var jsonString = Serialize(document, serializer);
        Assert.Contains(ChatInfoToChatSliceRelation.Name, jsonString, StringComparison.Ordinal);
        Assert.Contains(ChatInfoToChatSliceRelation.ChatSliceName, jsonString, StringComparison.Ordinal);
        Assert.Contains("_routing", jsonString, StringComparison.Ordinal);

        var deserializedDocument = Deserialize<ChatSlice>(jsonString, serializer);
        Assert.Equivalent(document, deserializedDocument);
    }

    [Fact]
    public void ChatInfoSerializesAndDeserializesProperly()
    {
        var client = AppHost.Services.GetRequiredService<IOpenSearchClient>();

        var chatId = new ChatId(Generate.Option);
        var document = new ChatInfo(chatId, true, true);

        var serializer = client.SourceSerializer;
        var jsonString = Serialize(document, serializer);
        Assert.Contains(ChatInfoToChatSliceRelation.Name, jsonString, StringComparison.Ordinal);
        Assert.Contains(ChatInfoToChatSliceRelation.ChatInfoName, jsonString, StringComparison.Ordinal);

        var deserializedDocument = Deserialize<ChatInfo>(jsonString, serializer);
        Assert.Equivalent(document, deserializedDocument);
    }

    private string Serialize<TDoc>(TDoc document, IOpenSearchSerializer serializer)
    {
        using var stream = new MemoryStream();
        serializer.Serialize(document, stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private TDoc Deserialize<TDoc>(string jsonString, IOpenSearchSerializer serializer)
    {
        var encoding = new UTF8Encoding(false);
        using var stream = new MemoryStream(encoding.GetBytes(jsonString));
        return serializer.Deserialize<TDoc>(stream);
    }
}
