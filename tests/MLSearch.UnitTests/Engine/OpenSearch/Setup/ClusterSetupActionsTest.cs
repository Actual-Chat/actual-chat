using System.Text;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using ActualChat.Performance;
using OpenSearch.Client;
using OpenSearch.Net;

using HttpMethod = OpenSearch.Net.HttpMethod;


namespace ActualChat.MLSearch.UnitTests.Engine.OpenSearch.Setup;

// Response to model Id request
// {
//   "took": 3,
//   "timed_out": false,
//   "_shards": {
//     "total": 1,
//     "successful": 1,
//     "skipped": 0,
//     "failed": 0
//   },
//   "hits": {
//     "total": {
//       "value": 1,
//       "relation": "eq"
//     },
//     "max_score": null,
//     "hits": [
//       {
//         "_index": ".plugins-ml-model-group",
//         "_id": "sQslDo8BjOZ4dQx_Hke0",
//         "_version": 2,
//         "_seq_no": 1,
//         "_primary_term": 1,
//         "_score": null,
//         "_source": {
//           "created_time": 1713929264559,
//           "access": "public",
//           "latest_version": 1,
//           "last_updated_time": 1713929265790,
//           "name": "NLP_model_group",
//           "description": "A model group for NLP models"
//         },
//         "sort": [
//           1
//         ]
//       }
//     ]
//   }
// }

// Response to model parameters request
// {
//   "took": 3,
//   "timed_out": false,
//   "_shards": {
//     "total": 1,
//     "successful": 1,
//     "skipped": 0,
//     "failed": 0
//   },
//   "hits": {
//     "total": {
//       "value": 9,
//       "relation": "eq"
//     },
//     "max_score": null,
//     "hits": [
//       {
//         "_index": ".plugins-ml-model",
//         "_id": "sgslDo8BjOZ4dQx_I0eB",
//         "_version": 22,
//         "_seq_no": 29,
//         "_primary_term": 7,
//         "_score": null,
//         "_source": {
//           "last_deployed_time": 1715924158689,
//           "model_version": "1",
//           "created_time": 1713929266047,
//           "deploy_to_all_nodes": true,
//           "model_format": "TORCH_SCRIPT",
//           "model_state": "DEPLOYED",
//           "planning_worker_node_count": 1,
//           "total_chunks": 8,
//           "model_content_hash_value": "2ffa1f98dc511ec793232a44c116aaac9455912b65a0a7299fd41e4a8f9f77b9",
//           "model_config": {
//             "all_config": """{"_name_or_path":"old_models/paraphrase-MiniLM-L3-v2/0_Transformer","architectures":["BertModel"],"attention_probs_dropout_prob":0.1,"gradient_checkpointing":false,"hidden_act":"gelu","hidden_dropout_prob":0.1,"hidden_size":384,"initializer_range":0.02,"intermediate_size":1536,"layer_norm_eps":1e-12,"max_position_embeddings":512,"model_type":"bert","num_attention_heads":12,"num_hidden_layers":3,"pad_token_id":0,"position_embedding_type":"absolute","transformers_version":"4.7.0","type_vocab_size":2,"use_cache":true,"vocab_size":30522}""",
//             "model_type": "bert",
//             "embedding_dimension": 384,
//             "framework_type": "SENTENCE_TRANSFORMERS"
//           },
//           "auto_redeploy_retry_times": 0,
//           "last_updated_time": 1715924158690,
//           "name": "sentence-transformers/paraphrase-MiniLM-L3-v2",
//           "current_worker_node_count": 1,
//           "model_group_id": "sQslDo8BjOZ4dQx_Hke0",
//           "model_content_size_in_bytes": 70401552,
//           "planning_worker_nodes": [
//             "tcvZRDqbQSuDz0vk7Dxf4A"
//           ],
//           "algorithm": "TEXT_EMBEDDING"
//         },
//         "sort": [
//           29
//         ]
//       }
//     ]
//   }
// }


// var createResponse = new CreateIndexResponse { Acknowledged = true, Index = "testing", ShardsAcknowledged = true };
// var mockedResponse = TestableResponseFactory.CreateSuccessfulResponse(createResponse, 201);

// var mockedClient = Mock.Of<ElasticsearchClient>(e =>
//     e.Indices.Create<It.IsAnyType>() == mockedResponse);

// var testResponse = mockedClient.Indices.Create<Person>();

// if (testResponse.IsValidResponse)
//     Console.WriteLine("SUCCESS");

public class ClusterSetupActionsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string ModelGroupName = "Test_Models";
    private const string ModelGroupId = "sQslDo8BjOZ4dQx_Hke0";
    private const string ModelId = "sgslDo8BjOZ4dQx_I0eB";
    private const string ModelAllConfig = "{ some: 'json config' }";
    private const int EmbeddingDimension = 1024;

    private readonly OpenSearchNamingPolicy _openSearchNamingPolicy = new(JsonNamingPolicy.CamelCase);

    private readonly string[] _retrieveModelPropsResponses = [
        $$"""
            { "hits": { "hits": [ { "_id": "{{ModelGroupId}}"} ]} }
        """,
        $$"""
        {
            "hits": {
                "hits": [
                    {
                        "_id": "{{ModelId}}",
                        "_source": {
                            "model_state": "DEPLOYED",
                            "model_config": {
                                "all_config": "{{ModelAllConfig}}",
                                "embedding_dimension": {{EmbeddingDimension}}
                            }
                        }
                    }
                ]
            }
        }
        """
    ];

    [Fact]
    public async Task CanRetrieveModelProps()
    {
        List<(int, string)> responses = [
            .. _retrieveModelPropsResponses.Select(r => (200, r))
        ];
        var actions = CreateActions(responses);

        var modelProps = await actions.RetrieveEmbeddingModelPropsAsync(ModelGroupName, CancellationToken.None);

        var expected = new EmbeddingModelProps(ModelId, EmbeddingDimension, ModelAllConfig);
        Assert.Equal(expected.Id, modelProps.Id);
        Assert.Equal(expected.EmbeddingDimension, modelProps.EmbeddingDimension);
        Assert.Equal(expected.UniqueKey, modelProps.UniqueKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RetrieveModelPropsThrowsOnUnsuccessfulOpenSearchCall(int successCount)
    {
        List<(int, string)> responses = [
            .. _retrieveModelPropsResponses.Take(successCount).Select(r => (200, r)),
            (500, "{}")
        ];
        var actions = CreateActions(responses);
        var exception = await Assert.ThrowsAsync<ExternalError>(
            () => actions.RetrieveEmbeddingModelPropsAsync(ModelGroupName, CancellationToken.None)
        );
        Assert.StartsWith("OpenSearch request failed", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RetrieveModelPropsThrowsOnMalformedOrEmptyResponse(int malformedId)
    {
        var malformedResponse = @"{ ""malformed"": { ""hits"": [] }}";
        var emptyResponse = @"{ ""hits"": { ""hits"": [] }}";
        foreach (var badResponse in new [] { malformedResponse, emptyResponse }) {
            List<(int, string)> responses = [
                .. _retrieveModelPropsResponses.Select((r, i) => (200, i == malformedId ? malformedResponse : r))
            ];
            var actions = CreateActions(responses);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => actions.RetrieveEmbeddingModelPropsAsync(ModelGroupName, CancellationToken.None)
            );
            Assert.StartsWith("Query result is malformed or empty", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RetrieveModelPropsThrowsIfNoModelGroupIdFound()
    {
        List<(int, string)> responses = [
            (200, @"{ ""hits"": { ""hits"": [ {} ] }}") // There is no '_id' property in the first hit
        ];
        var actions = CreateActions(responses);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actions.RetrieveEmbeddingModelPropsAsync(ModelGroupName, CancellationToken.None)
        );
        Assert.StartsWith("Failed to retrieve model group id.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrieveModelPropsThrowsIfNoModelIdFound()
    {
        List<(int, string)> responses = [
            (200, _retrieveModelPropsResponses[0]),
            (200, @"{ ""hits"": { ""hits"": [ {} ] }}") // There is no '_id' property in the first hit
        ];
        var actions = CreateActions(responses);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actions.RetrieveEmbeddingModelPropsAsync(ModelGroupName, CancellationToken.None)
        );
        Assert.StartsWith("Failed to retrieve model id.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrieveModelPropsThrowsIfNoSourceFound()
    {
        List<(int, string)> responses = [
            (200, _retrieveModelPropsResponses[0]),
            (200, @"{ ""hits"": { ""hits"": [ { ""_id"": ""some_id"" } ] }}") // There is no '_id' property in the first hit
        ];
        var actions = CreateActions(responses);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actions.RetrieveEmbeddingModelPropsAsync(ModelGroupName, CancellationToken.None)
        );
        Assert.StartsWith("_source is null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrieveModelPropsThrowsIfSourceDoesntHaveModelState()
    {
        List<(int, string)> responses = [
            (200, _retrieveModelPropsResponses[0]),
            (
                200,
                $$"""
                {
                    "hits": {
                        "hits": [
                            {
                                "_id": "{{ModelId}}",
                                "_source": { }
                            }
                        ]
                    }
                }
                """
            )
        ];
        var actions = CreateActions(responses);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actions.RetrieveEmbeddingModelPropsAsync(ModelGroupName, CancellationToken.None)
        );
        Assert.StartsWith("model_state field is not found", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("UPLOADED")]
    public async Task RetrieveModelPropsThrowsIfModelIsInImproperState(string modelState)
    {
        List<(int, string)> responses = [
            (200, _retrieveModelPropsResponses[0]),
            (
                200,
                $$"""
                {
                    "hits": {
                        "hits": [
                            {
                                "_id": "{{ModelId}}",
                                "_source": {
                                    "model_state": "{{modelState}}"
                                }
                            }
                        ]
                    }
                }
                """
            )
        ];
        var actions = CreateActions(responses);
        var exception = await Assert.ThrowsAsync<ExternalError>(
            () => actions.RetrieveEmbeddingModelPropsAsync(ModelGroupName, CancellationToken.None)
        );
        Assert.StartsWith("Invalid model state", exception.Message, StringComparison.Ordinal);
        Assert.Contains(string.IsNullOrEmpty(modelState) ? "<Empty>" : modelState, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrieveModelPropsThrowsIfSourceDoesntHaveModelConfig()
    {
        List<(int, string)> responses = [
            (200, _retrieveModelPropsResponses[0]),
            (
                200,
                $$"""
                {
                    "hits": {
                        "hits": [
                            {
                                "_id": "{{ModelId}}",
                                "_source": {
                                    "model_state": "DEPLOYED"
                                }
                            }
                        ]
                    }
                }
                """
            )
        ];
        var actions = CreateActions(responses);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actions.RetrieveEmbeddingModelPropsAsync(ModelGroupName, CancellationToken.None)
        );
        Assert.StartsWith("model_config is null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrieveModelPropsThrowsIfNoEmbeddingDimensionFound()
    {
        List<(int, string)> responses = [
            (200, _retrieveModelPropsResponses[0]),
            (
                200,
                $$"""
                {
                    "hits": {
                        "hits": [
                            {
                                "_id": "{{ModelId}}",
                                "_source": {
                                    "model_state": "DEPLOYED",
                                    "model_config": {}
                                }
                            }
                        ]
                    }
                }
                """
            )
        ];
        var actions = CreateActions(responses);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actions.RetrieveEmbeddingModelPropsAsync(ModelGroupName, CancellationToken.None)
        );
        Assert.StartsWith("Failed to retrieve model embedding dimension value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrieveModelPropsThrowsIfNoFullModelConfigFound()
    {
        List<(int, string)> responses = [
            (200, _retrieveModelPropsResponses[0]),
            (
                200,
                $$"""
                {
                    "hits": {
                        "hits": [
                            {
                                "_id": "{{ModelId}}",
                                "_source": {
                                    "model_state": "DEPLOYED",
                                    "model_config": {
                                        "embedding_dimension": {{EmbeddingDimension}}
                                    }
                                }
                            }
                        ]
                    }
                }
                """
            )
        ];
        var actions = CreateActions(responses);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actions.RetrieveEmbeddingModelPropsAsync(ModelGroupName, CancellationToken.None)
        );
        Assert.StartsWith("Failed to retrieve model all_config value", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IsTemplateValidAsync")]
    [InlineData("IsPipelineExistsAsync")]
    [InlineData("IsIndexExistsAsync")]
    [InlineData("EnsureTemplateAsync")]
    [InlineData("EnsureEmbeddingIngestPipelineAsync")]
    [InlineData("EnsureContentIndexAsync")]
    [InlineData("EnsureContentCursorIndexAsync")]
    [InlineData("EnsureChatsCursorIndexAsync")]
    public async Task ActionsThrowOnFirstUnsuccessfulOpenSearchCall(string actionName)
    {
        Func<IClusterSetupActions, Task> callAction = actionName switch {
            "IsTemplateValidAsync" =>
                (IClusterSetupActions actions) => actions.IsTemplateValidAsync("some_template", "*", 0, CancellationToken.None),
            "IsPipelineExistsAsync" =>
                (IClusterSetupActions actions) => actions.IsPipelineExistsAsync("some_pipeline", CancellationToken.None),
            "IsIndexExistsAsync" =>
                (IClusterSetupActions actions) => actions.IsIndexExistsAsync("some_index", CancellationToken.None),
            "EnsureTemplateAsync" =>
                (IClusterSetupActions actions) => actions.EnsureTemplateAsync("some_template", "*", 0, CancellationToken.None),
            "EnsureEmbeddingIngestPipelineAsync" =>
                (IClusterSetupActions actions) => actions.EnsureEmbeddingIngestPipelineAsync("some_pipeline", ModelId, "text", CancellationToken.None),
            "EnsureContentIndexAsync" =>
                (IClusterSetupActions actions) => actions.EnsureContentIndexAsync("some_index", "some_pipeline", 1024, CancellationToken.None),
            "EnsureContentCursorIndexAsync" =>
                (IClusterSetupActions actions) => actions.EnsureContentCursorIndexAsync("some_index", CancellationToken.None),
            "EnsureChatsCursorIndexAsync" =>
                (IClusterSetupActions actions) => actions.EnsureChatsCursorIndexAsync("some_index", CancellationToken.None),
            _ => throw new NotSupportedException($"Action '{actionName}' is not supported.")
        };

        List<(int, string)> responses = [
            (500, "{}")
        ];
        var actions = CreateActions(responses);
        var exception = await Assert.ThrowsAsync<ExternalError>(
            () => callAction(actions)
        );
        Assert.StartsWith("OpenSearch request failed", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IsTemplateValidAsync")]
    [InlineData("IsPipelineExistsAsync")]
    [InlineData("IsIndexExistsAsync")]
    public async Task CheckActionsResultToFalseOn_404_Response(string actionName)
    {
        Func<IClusterSetupActions, Task<bool>> callAction = actionName switch {
            "IsTemplateValidAsync" =>
                (IClusterSetupActions actions) => actions.IsTemplateValidAsync("some_template", "*", 0, CancellationToken.None),
            "IsPipelineExistsAsync" =>
                (IClusterSetupActions actions) => actions.IsPipelineExistsAsync("some_pipeline", CancellationToken.None),
            "IsIndexExistsAsync" =>
                (IClusterSetupActions actions) => actions.IsIndexExistsAsync("some_index", CancellationToken.None),
            _ => throw new NotSupportedException($"Action '{actionName}' is not supported.")
        };

        List<(int, string)> responses = [
            (404, "{}")
        ];
        var actions = CreateActions(responses);
        var checkResult = await callAction(actions);
        Assert.False(checkResult);
    }

    private ClusterSetupActions CreateActions(List<(int, string)> responses)
    {
        var client = new OpenSearchClient(new ConnectionSettings(
            new SingleNodeConnectionPool(new Uri("fake://host:9200")),
            new TestableInMemoryConnection(a => { }, responses)
        ));

        return new ClusterSetupActions(client, _openSearchNamingPolicy, Tracer.None);
    }
}

internal class TestableInMemoryConnection : IConnection
{
    internal static readonly byte[] EmptyBody = Encoding.UTF8.GetBytes("");

    private readonly Action<RequestData> _perRequestAssertion;
    private readonly List<(int, string)> _responses;
    private int _requestCounter = -1;

    public TestableInMemoryConnection(Action<RequestData> assertion, List<(int, string)> responses)
    {
        _perRequestAssertion = assertion;
        _responses = responses;
    }

    public void AssertExpectedCallCount() => _requestCounter.Should().Be(_responses.Count - 1);

    async Task<TResponse> IConnection.RequestAsync<TResponse>(RequestData requestData, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCounter);

        _perRequestAssertion(requestData);

        await Task.Yield(); // avoids test deadlocks

        int statusCode;
        string response;

        if (_responses.Count > _requestCounter)
            (statusCode, response) = _responses[_requestCounter];
        else
            (statusCode, response) = (500, (string)null);

        var stream = !string.IsNullOrEmpty(response) ? requestData.MemoryStreamFactory.Create(Encoding.UTF8.GetBytes(response)) : requestData.MemoryStreamFactory.Create(EmptyBody);

        return await ResponseBuilder
            .ToResponseAsync<TResponse>(requestData, null, statusCode, null, stream, RequestData.MimeType, cancellationToken)
            .ConfigureAwait(false);
    }

    TResponse IConnection.Request<TResponse>(RequestData requestData)
    {
        Interlocked.Increment(ref _requestCounter);

        _perRequestAssertion(requestData);

        int statusCode;
        string response;

        if (_responses.Count > _requestCounter)
            (statusCode, response) = _responses[_requestCounter];
        else
            (statusCode, response) = (200, (string)null);

        var stream = !string.IsNullOrEmpty(response) ? requestData.MemoryStreamFactory.Create(Encoding.UTF8.GetBytes(response)) : requestData.MemoryStreamFactory.Create(EmptyBody);

        return ResponseBuilder.ToResponse<TResponse>(requestData, null, statusCode, null, stream, RequestData.MimeType);
    }

    public void Dispose() { }
}


        // var openSearchLowLevelMock = new Mock<IOpenSearchLowLevelClient>();
        // openSearchLowLevelMock
        //     .Setup(os => os.DoRequestAsync<DynamicResponse>(
        //         It.Is<HttpMethod>(method => method == HttpMethod.POST),
        //         It.Is<string>(s => s.StartsWith("/_plugins/_ml/model_groups/_search", StringComparison.Ordinal)),
        //         It.IsAny<CancellationToken>(),
        //         It.IsAny<PostData>(),
        //         It.IsAny<IRequestParameters>()
        //     ))
        //     .Returns(() => {
        //         var responseItems = DynamicDictionary.Create(new Dictionary<string, object>() {
        //             "hits.hits"
        //         });
        //         var response = new DynamicResponse(responseItems);
        //         return Task.FromResult(TestableResponseFactory.CreateSuccessfulResponse(response, 200));
        //     });
        // var openSearchMock = new Mock<IOpenSearchClient>();
        // openSearchMock.SetupGet(os => os.LowLevel).Returns(openSearchLowLevelMock.Object);

        // var actions = new ClusterSetupActions(openSearchMock.Object, _openSearchNamingPolicy, Tracer.None);


public static class TestableResponseFactory
{
    public static T CreateSuccessfulResponse<T>(T response, int httpStatusCode) where T : IOpenSearchResponse
        => CreateResponse(response, httpStatusCode, true);

    public static T CreateResponse<T>(T response, int httpStatusCode, bool statusCodeRepresentsSuccess) where T : IOpenSearchResponse
    {
        var apiCallDetails = new ApiCallDetails
        {
            HttpStatusCode = httpStatusCode,
            Success = statusCodeRepresentsSuccess,
        };

        return CreateResponse<T>(response, apiCallDetails);
    }

    internal static T CreateResponse<T>(T response, ApiCallDetails apiCallDetails) where T : IOpenSearchResponse
    {
        response.ApiCall = apiCallDetails;
        return response;
    }
}
