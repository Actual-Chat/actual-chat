using ActualChat.MLSearch.SearchEngine.OpenSearch;
using ActualChat.Performance;
using ActualChat.Testing.Host;
using Mjml.Net.Extensions;
using OpenSearch.Client;
using OpenSearch.Net;
using HttpMethod = OpenSearch.Net.HttpMethod;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
public class OpenSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
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
        var modelId = settings!.ModelId;
        var searchIndexId = settings.IntoSearchIndexId();
        var documents = new [] {
            new IndexedDocument { Uri="message_01", Text="OpenSearch supports the following models, categorized by type." },
            new IndexedDocument { Uri="message_02", Text="Quite often, our methods are async, and we can't make constructors async. This is where XUnit's IAsyncLifetime comes in." },
            new IndexedDocument { Uri="message_03", Text="The license for Docker Desktop is not expensive, but it's not free anymore, so I wanted to check if there's an easy and cheap alternative to Docker Desktop for Windows." },
            new IndexedDocument { Uri="message_04", Text="The sdkmanager is a command-line tool that lets you view, install, update, and uninstall packages for the Android SDK." },
            new IndexedDocument { Uri="message_05", Text="In Barcelona, a migrant squatter was asked to leave by the property owner. He refused and threatened the home owner with a hammer." },
        };
        for (var i=0; i<documents.Length; i++) {
            var docId = (i + 1).ToInvariantString();
            var newDocResponse = await client!.LowLevel.DoRequestAsync<StringResponse>(
                HttpMethod.PUT, $"/{searchIndexId}/_doc/{docId}", CancellationToken.None, PostData.Serializable(documents[i]));
            Assert.True(newDocResponse.Success);
        }

        var queryResponse = await client!.LowLevel.DoRequestAsync<StringResponse>(HttpMethod.GET, $"/{searchIndexId}/_search", CancellationToken.None,
            PostData.String(
                $$"""
                {
                    "_source": {
                        "excludes": [
                            "event_dense_embedding"
                        ]
                    },
                    "query": {
                        "bool": {
                            "filter": {
                                "wildcard":  { "{{nameof(IndexedDocument.Uri)}}": "*ess*" }
                            },
                            "should": [
                                {
                                    "script_score": {
                                        "query": {
                                        "neural": {
                                            "event_dense_embedding": {
                                                "query_text": "Tools for mobile development",
                                                    "model_id": "{{modelId}}",
                                                    "k": 100
                                                }
                                            }
                                        },
                                        "script": {
                                            "source": "_score * 1.5"
                                        }
                                    }
                                },
                                {
                                "script_score": {
                                    "query": {
                                        "match": {
                                            "{{nameof(IndexedDocument.Text)}}": "command"
                                        }
                                    },
                                    "script": {
                                        "source": "_score * 1.7"
                                    }
                                }
                                }
                            ]
                        }
                    }
                }
                """
            )
        );
        Assert.True(queryResponse.Success);
        Out.WriteLine(queryResponse.Body);
    }
}
