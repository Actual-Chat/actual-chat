using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using ActualChat.Performance;
using OpenSearch.Client;
using OpenSearch.Net;

using HttpMethod = OpenSearch.Net.HttpMethod;

namespace ActualChat.MLSearch.UnitTests.Engine.OpenSearch.Setup;

public class ClusterSetupActionsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string ModelGroupName = "Test_Models";
    private const string ModelGroupId = "sQslDo8BjOZ4dQx_Hke0";
    private const string ModelId = "sgslDo8BjOZ4dQx_I0eB";
    private const string ModelAllConfig = "{ some: 'json config' }";
    private const int EmbeddingDimension = 1024;

    private const string CheckTemplateAction = nameof(IClusterSetupActions.IsTemplateValidAsync);
    private const string CheckPipelineAction = nameof(IClusterSetupActions.IsPipelineExistsAsync);
    private const string CheckIndexAction = nameof(IClusterSetupActions.IsIndexExistsAsync);
    private const string EnsureTemplateAction = nameof(IClusterSetupActions.EnsureTemplateAsync);
    private const string EnsureIngestPipelineAction = nameof(IClusterSetupActions.EnsureEmbeddingIngestPipelineAsync);
    private const string EnsureContentCursorIndexAction = nameof(IClusterSetupActions.EnsureContentCursorIndexAsync);
    private const string EnsureContentIndexAction = nameof(IClusterSetupActions.EnsureContentIndexAsync);
    private const string EnsureChatsCursorIndexAction = nameof(IClusterSetupActions.EnsureChatsCursorIndexAsync);

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
        """,
    ];

    [Fact]
    public async Task CanRetrieveModelProps()
    {
        List<(int, string)> responses = [
            .. _retrieveModelPropsResponses.Select(r => (200, r)),
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
            (500, "{}"),
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
    public async Task RetrieveModelPropsThrowsOnMalformedOrEmptyResponse(int badId)
    {
        const string malformedResponse = """{ "malformed": { "hits": [] }}""";
        const string emptyResponse = """{ "hits": { "hits": [] }}""";
        foreach (var badResponse in new [] { malformedResponse, emptyResponse }) {
            List<(int, string)> responses = [
                .. _retrieveModelPropsResponses.Select((r, i) => (200, i == badId ? badResponse : r)),
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
            (200, """{ "hits": { "hits": [ {} ] } }"""), // There is no '_id' property in the first hit
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
            (200, """{ "hits": { "hits": [ {} ] } }"""), // There is no '_id' property in the first hit
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
            (200, """{ "hits": { "hits": [ { "_id": "some_id" } ] }}"""), // There is no '_id' property in the first hit
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
            ),
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
            ),
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
            ),
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
            ),
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
            ),
        ];
        var actions = CreateActions(responses);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actions.RetrieveEmbeddingModelPropsAsync(ModelGroupName, CancellationToken.None)
        );
        Assert.StartsWith("Failed to retrieve model all_config value", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CheckTemplateAction)]
    [InlineData(CheckPipelineAction)]
    [InlineData(CheckIndexAction)]
    [InlineData(EnsureTemplateAction)]
    [InlineData(EnsureIngestPipelineAction)]
    [InlineData(EnsureContentIndexAction)]
    [InlineData(EnsureContentCursorIndexAction)]
    [InlineData(EnsureChatsCursorIndexAction)]
    public async Task ActionsThrowOnFirstUnsuccessfulOpenSearchCall(string actionName)
    {
        var callAction = GetCallAction(actionName, "some_entity");
        var actions = CreateActions([ (500, "{}") ]);
        var exception = await Assert.ThrowsAsync<ExternalError>(
            () => callAction(actions)
        );
        Assert.StartsWith("OpenSearch request failed", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EnsureTemplateAction)]
    [InlineData(EnsureIngestPipelineAction)]
    [InlineData(EnsureContentIndexAction)]
    [InlineData(EnsureContentCursorIndexAction)]
    [InlineData(EnsureChatsCursorIndexAction)]
    public async Task ActionsThrowOnUnsuccessfulEntityCreation(string actionName)
    {
        var callAction = GetCallAction(actionName, "some_entity");
        List<(int, string)> responses = [
            (404, "{}"),
            (500, "{}"),
        ];
        var actions = CreateActions(responses);
        var exception = await Assert.ThrowsAsync<ExternalError>(
            () => callAction(actions)
        );
        Assert.StartsWith("OpenSearch request failed", exception.Message, StringComparison.Ordinal);
    }


    [Theory]
    [InlineData(CheckTemplateAction)]
    [InlineData(CheckPipelineAction)]
    [InlineData(CheckIndexAction)]
    public async Task CheckActionsResultToFalseOn_404_Response(string actionName)
    {
        var callAction = GetCallCheckAction(actionName, "some_entity");
        var actions = CreateActions([ (404, "{}") ]);
        var checkResult = await callAction(actions);
        Assert.False(checkResult);
    }

    public class IsTemplateValidParams(string name, string pattern, int? numberOfReplicas) : IXunitSerializable
    {
        public string Name => name;
        public string Pattern => pattern;
        public int? NumberOfReplicas => numberOfReplicas;

        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public IsTemplateValidParams() : this(string.Empty, string.Empty, default)
        { }

        public void Deserialize(IXunitSerializationInfo info)
        {
            name = info.GetValue<string>(nameof(name));
            pattern = info.GetValue<string>(nameof(pattern));
            numberOfReplicas = info.GetValue<int?>(nameof(numberOfReplicas));
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(name), name);
            info.AddValue(nameof(pattern), pattern);
            info.AddValue(nameof(numberOfReplicas), numberOfReplicas);
        }
    }

    public static TheoryData<bool, IsTemplateValidParams, IsTemplateValidParams> TemplateChecks => new() {
        { true, new("ml-template", "ml-*", 0), new("ml-template", "ml-*", 0) },
        { true, new("ml-template", "ml-*", null), new("ml-template", "ml-*", null) },
        { false, new("ml-template", "ml-*", 0), new("other-template", "ml-*", 0) },
        { false, new("ml-template", "ml-*", 0), new("ml-template", "other-*", 0) },
        { false, new("ml-template", "ml-*", 0), new("ml-template", "ml-*", 1) },
        { false, new("ml-template", "ml-*", 0), new("ml-template", "ml-*", null) },
        { false, new("ml-template", "ml-*", null), new("ml-template", "ml-*", 0) },
    };

    [Theory]
    [MemberData(nameof(TemplateChecks))]
    public async Task IsTemplateValidAsyncChecksIfTemplateExistsAndHasExpectedProps(
        bool expected,
        IsTemplateValidParams responseParams,
        IsTemplateValidParams callParams
    )
    {
        var numberOfReplicas = responseParams.NumberOfReplicas;
        var numberOfReplicasSetting = numberOfReplicas.HasValue ? $"\"number_of_replicas\": \"{numberOfReplicas}\"" : "";
        var response =
            $$"""
            {
                "{{responseParams.Name}}": {
                    "order": 0,
                    "index_patterns": [
                        "{{responseParams.Pattern}}"
                    ],
                    "settings": {
                        "index": {
                            {{numberOfReplicasSetting}}
                        }
                    },
                    "mappings": {},
                    "aliases": {}
                }
            }
            """;
        var actions = CreateActions([ (200, response) ]);
        var result = await actions.IsTemplateValidAsync(callParams.Name, callParams.Pattern, callParams.NumberOfReplicas, CancellationToken.None);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("existing-pipeline")]
    [InlineData("other-pipeline")]
    public async Task IsPipelineExistsAsyncChecksIfPipelineExists(string pipelineName)
    {
        const string existingPipelineName = "existing-pipeline";
        const string response =
            $$"""
            {
                "{{existingPipelineName}}": {
                    "description": "Autogenerated pipeline"
                }
            }
            """;
        var actions = CreateActions([ (200, response) ]);
        var result = await actions.IsPipelineExistsAsync(pipelineName, CancellationToken.None);
        Assert.Equal(existingPipelineName.Equals(pipelineName, StringComparison.Ordinal), result);
    }

    [Theory]
    [InlineData("existing-index")]
    [InlineData("other-index")]
    public async Task IsIndexExistsAsyncChecksIfIndexExists(string indexName)
    {
        const string existingIndex = "existing-index";
        var expected = existingIndex.Equals(indexName, StringComparison.Ordinal);
        List<(int, string)> responses = [
            expected ? (200, string.Empty) : (404, "{}"),
        ];
        var actions = CreateActions(responses);
        var result = await actions.IsIndexExistsAsync(indexName, CancellationToken.None);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task EnsureTemplateAsyncDoesNotRecreateTemplateIfCheckSuccessful()
    {
        var (name, pattern, numberOfReplicas) = ("test", "test-*", 1024);
        var response =
            $$"""
            {
                "{{name}}": {
                    "order": 0,
                    "index_patterns": [
                        "{{pattern}}"
                    ],
                    "settings": {
                        "index": {
                            "number_of_replicas": "{{numberOfReplicas}}"
                        }
                    },
                    "mappings": {},
                    "aliases": {}
                }
            }
            """;
        var connection = new TestableInMemoryOpenSearchConnection(_ => { }, [ (200, response) ]);
        var actions = CreateActions(connection);

        await actions.EnsureTemplateAsync(name, pattern, numberOfReplicas, CancellationToken.None);

        connection.AssertExpectedCallCount();
    }

    [Fact]
    public async Task EnsureTemplateAsyncRecreatesTemplateIfNotFound()
    {
        var (name, pattern, numberOfReplicas) = ("test", "test-*", 1024);
        List<(int, string)> responses = [
            (404, "{}"),
            (200, "{ \"acknowledged\": true }"),
        ];
        var expectedRequests = new HashSet<(HttpMethod, string)> {
            (HttpMethod.GET, "_template/test"),
            (HttpMethod.PUT, "_template/test"),
        };

        var connection = new TestableInMemoryOpenSearchConnection(AssertRequest, responses);
        var actions = CreateActions(connection);

        await actions.EnsureTemplateAsync(name, pattern, numberOfReplicas, CancellationToken.None);

        connection.AssertExpectedCallCount();
        return;

        void AssertRequest(RequestData requestData)
            => Assert.True(expectedRequests.Remove((requestData.Method, requestData.PathAndQuery)));
    }

    [Fact]
    public async Task EnsureEmbeddingIngestPipelineAsyncDoesNotRecreatePipelineIfCheckSuccessful()
    {
        const string pipelineName = "test-pipeline";
        const string modelId = "model-xxx";
        const string textField = "text";
        const string response =
            $$"""
            {
                "{{pipelineName}}": {
                  "description": "Autogenerated pipeline"
                }
            }
            """;
        var connection = new TestableInMemoryOpenSearchConnection(_ => { }, [ (200, response) ]);
        var actions = CreateActions(connection);

        await actions.EnsureEmbeddingIngestPipelineAsync(pipelineName, modelId, textField, CancellationToken.None);

        connection.AssertExpectedCallCount();
    }

    [Fact]
    public async Task EnsureEmbeddingIngestPipelineAsyncRecreatesPipelineIfNotFound()
    {
        const string pipelineName = "test-pipeline";
        const string modelId = "model-xxx";
        const string textField = "text";
        List<(int, string)> responses = [
            (404, "{}"),
            (200, "{ \"acknowledged\": true }"),
        ];
        var expectedRequests = new HashSet<(HttpMethod, string)> {
            (HttpMethod.GET, $"_ingest/pipeline/{pipelineName}"),
            (HttpMethod.PUT, $"_ingest/pipeline/{pipelineName}"),
        };

        var connection = new TestableInMemoryOpenSearchConnection(AssertRequest, responses);
        var actions = CreateActions(connection);

        await actions.EnsureEmbeddingIngestPipelineAsync(pipelineName, modelId, textField, CancellationToken.None);

        connection.AssertExpectedCallCount();
        return;

        void AssertRequest(RequestData requestData)
            => Assert.True(expectedRequests.Remove((requestData.Method, requestData.PathAndQuery.Trim('/'))));
    }

    [Theory]
    [InlineData(EnsureContentIndexAction)]
    [InlineData(EnsureContentCursorIndexAction)]
    [InlineData(EnsureChatsCursorIndexAction)]
    public async Task EnsureIndexActionsDoNotRecreateIndexIfCheckIsSuccessful(string actionName)
    {
        var callAction = GetCallAction(actionName, "some_index");
        var connection = new TestableInMemoryOpenSearchConnection(_ => { }, [ (200, string.Empty) ]);
        var actions = CreateActions(connection);

        await callAction(actions);

        connection.AssertExpectedCallCount();
    }

    [Theory]
    [InlineData(EnsureContentIndexAction)]
    [InlineData(EnsureContentCursorIndexAction)]
    [InlineData(EnsureChatsCursorIndexAction)]
    public async Task EnsureIndexActionsRecreateIndexIfNotFound(string actionName)
    {
        const string indexName = "some_index";
        var callAction = GetCallAction(actionName, indexName);

        List<(int, string)> responses = [
            (404, "{}"),
            (200, "{ \"acknowledged\": true }"),
        ];
        var expectedRequests = new HashSet<(HttpMethod, string)> {
            (HttpMethod.HEAD, indexName),
            (HttpMethod.PUT, indexName),
        };

        var connection = new TestableInMemoryOpenSearchConnection(AssertRequest, responses);
        var actions = CreateActions(connection);

        await callAction(actions);

        connection.AssertExpectedCallCount();
        return;

        void AssertRequest(RequestData requestData)
            => Assert.True(expectedRequests.Remove((requestData.Method, requestData.PathAndQuery.Trim('/'))));
    }

    private readonly IConnectionPool _connectionPool = new SingleNodeConnectionPool(new Uri("fake://host:9200"));
    private readonly OpenSearchNamingPolicy _openSearchNamingPolicy = new(JsonNamingPolicy.CamelCase);

    private ClusterSetupActions CreateActions(TestableInMemoryOpenSearchConnection connection)
    {
        var client = new OpenSearchClient(
            new ConnectionSettings(_connectionPool, connection)
        );
        return new ClusterSetupActions(client, _openSearchNamingPolicy, Tracer.None);
    }
    private ClusterSetupActions CreateActions(List<(int, string)> responses)
        => CreateActions(new TestableInMemoryOpenSearchConnection(_ => { }, responses));

    private static Func<IClusterSetupActions, Task> GetCallAction(string actionName, string entityName) => actionName switch {
        CheckTemplateAction or CheckPipelineAction or CheckIndexAction
            => GetCallCheckAction(actionName, entityName),
        EnsureTemplateAction =>
            actions => actions.EnsureTemplateAsync(entityName, "*", 0, CancellationToken.None),
        EnsureIngestPipelineAction =>
            actions => actions.EnsureEmbeddingIngestPipelineAsync(entityName, ModelId, "text", CancellationToken.None),
        EnsureContentIndexAction =>
            actions => actions.EnsureContentIndexAsync(entityName, "some_pipeline", 1024, CancellationToken.None),
        EnsureContentCursorIndexAction =>
            actions => actions.EnsureContentCursorIndexAsync(entityName, CancellationToken.None),
        EnsureChatsCursorIndexAction =>
            actions => actions.EnsureChatsCursorIndexAsync(entityName, CancellationToken.None),
        _ => throw new NotSupportedException($"Action '{actionName}' is not supported."),
    };

    private static Func<IClusterSetupActions, Task<bool>> GetCallCheckAction(string actionName, string entityName) => actionName switch {
        CheckTemplateAction =>
            actions => actions.IsTemplateValidAsync(entityName, "*", 0, CancellationToken.None),
        CheckPipelineAction =>
            actions => actions.IsPipelineExistsAsync(entityName, CancellationToken.None),
        CheckIndexAction =>
            actions => actions.IsIndexExistsAsync(entityName, CancellationToken.None),
        _ => throw new NotSupportedException($"Action '{actionName}' is not supported."),
    };
}
