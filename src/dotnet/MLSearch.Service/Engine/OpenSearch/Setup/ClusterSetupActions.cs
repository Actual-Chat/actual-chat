using System.Security.Cryptography;
using System.Text;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.MLSearch.Indexing.Initializer;
using ActualChat.MLSearch.Module;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.MLSearch.Engine.OpenSearch.Setup;

internal interface IClusterSetupActions
{
    Task<EmbeddingModelProps> EnsureEmbeddingModelDeployedAsync(OpenSearchSettings value, CancellationToken cancellationToken);
    Task<bool> IsTemplateValidAsync(string templateName, string pattern, int? numberOfReplicas, CancellationToken cancellationToken);
    Task<bool> IsPipelineExistsAsync(string pipelineName, CancellationToken cancellationToken);
    Task<bool> IsIndexExistsAsync(string indexName, CancellationToken cancellationToken);
    Task EnsureTemplateAsync(string templateName, string pattern, int? numberOfReplicas, CancellationToken cancellationToken);
    Task EnsureEmbeddingIngestPipelineAsync(string pipelineName, string modelId, string textField, CancellationToken cancellationToken);
    Task EnsureContentIndexAsync(string indexName, string ingestPipelineName, int modelDimension, CancellationToken cancellationToken);
    Task EnsureContentCursorIndexAsync(string indexName, CancellationToken cancellationToken);
    Task EnsureChatsCursorIndexAsync(string indexName, CancellationToken cancellationToken);
}

internal abstract class ClusterSetupActions(
    IOpenSearchClient openSearch,
    OpenSearchNamingPolicy namingPolicy,
    Tracer baseTracer
) : IClusterSetupActions
{
    private const string TimestampFieldName = "timestamp";
    protected readonly Tracer _tracer = baseTracer[typeof(ClusterSetup)];

    protected async Task<string?> GetModelGroupId(OpenSearchSettings openSearchSettings, CancellationToken cancellationToken)
    {
        var modelGroupName = openSearchSettings.ModelGroup;

        // Read model group latest state
        var modelGroupResponse = await openSearch.RunAsync(
                $$"""
                POST /_plugins/_ml/model_groups/_search
                {
                    "query": {
                        "match": {
                            "name": "{{modelGroupName}}"
                        }
                    },
                    "sort": [{
                        "_seq_no": { "order": "desc" }
                    }],
                    "size": 1
                }
                """,
                cancellationToken
            )
            .ConfigureAwait(false);
        var modelGroup = modelGroupResponse
            .AssertSuccess()
            .FirstHit();
        var modelGroupId = modelGroup.Get<string>("_id");
        if (modelGroupId.IsNullOrEmpty()) {
            throw new InvalidOperationException(
                "Failed to retrieve model group id."
            );
        }

        return modelGroupId;
    }

    public async Task<EmbeddingModelProps> EnsureEmbeddingModelDeployedAsync(OpenSearchSettings value, CancellationToken cancellationToken)
    {
        var modelProps = await RetrieveEmbeddingModelPropsAsync(value, cancellationToken).ConfigureAwait(false);

        await EnsureModelDeployedAsync(modelProps.Id, cancellationToken).ConfigureAwait(false);

        return modelProps;
    }

    private async Task EnsureModelDeployedAsync(string modelId, CancellationToken cancellationToken)
    {
        var modelResponse = await openSearch.RunAsync($"GET /_plugins/_ml/models/{modelId}", cancellationToken)
            .ConfigureAwait(false);

        if (!OrdinalEquals(modelResponse.AssertSuccess().Get<string>("model_state"), "DEPLOYED")) {
            // Deploy the ML model
            var deployModelResponse = await openSearch.Http
                .PostAsync<DynamicResponse>($"/_plugins/_ml/models/{modelId}/_deploy", ct: cancellationToken)
                .ConfigureAwait(false);

            deployModelResponse.AssertSuccess();

            var modelDeployTaskStatus = (string)deployModelResponse.Body.status;
            if (!OrdinalEquals(modelDeployTaskStatus, "CREATED"))
                throw StandardError.External("Model deploy task is not CREATED.");

            var modelDeployTaskId = (string) deployModelResponse.Body.task_id;

            // Wait for ML model deployment to complete
            while (true) {
                var getTaskResponse = await openSearch.Http
                    .GetAsync<DynamicResponse>($"/_plugins/_ml/tasks/{modelDeployTaskId}", ct: cancellationToken)
                    .ConfigureAwait(false);

                getTaskResponse.AssertSuccess();

                var modelDeployTaskState = (string)getTaskResponse.Body.state;
                if (OrdinalEquals(modelDeployTaskState, "FAILED"))
                    throw StandardError.External("Model deploy task has FAILED.");

                if (modelDeployTaskState.OrdinalStartsWith("COMPLETED"))
                    break;

                await Task.Delay(2000, cancellationToken);
            }
        }
    }

    protected abstract Task<EmbeddingModelProps> RetrieveEmbeddingModelPropsAsync(
        OpenSearchSettings openSearchSettings, CancellationToken cancellationToken);

    public async Task EnsureTemplateAsync(string templateName, string pattern, int? numberOfReplicas, CancellationToken cancellationToken)
    {
        var isValidTemplate = await IsTemplateValidAsync(templateName, pattern, numberOfReplicas, cancellationToken)
            .ConfigureAwait(false);
        if (!isValidTemplate) {
            var result = await openSearch.Indices
                .PutTemplateAsync(
                    templateName,
                    index => index
                        .Settings(s => s.NumberOfReplicas(numberOfReplicas))
                        .IndexPatterns(pattern),
                    cancellationToken
                )
                .ConfigureAwait(false);
            result.AssertSuccess();
        }
    }

    public async Task EnsureContentIndexAsync(string indexName, string ingestPipelineName, int modelDimension, CancellationToken cancellationToken)
    {
        var isSearchIndexExists = await IsIndexExistsAsync(indexName, cancellationToken).ConfigureAwait(false);
        if (!isSearchIndexExists) {
            // Calculate field names
            // ChatInfo fields
            var isPublicField = namingPolicy.ConvertName(nameof(ChatInfo.IsPublic));
            var isBotChatField = namingPolicy.ConvertName(nameof(ChatInfo.IsBotChat));
            // ChatSlice fields
            var metadataField = namingPolicy.ConvertName(nameof(ChatSlice.Metadata));
            var textField = namingPolicy.ConvertName(nameof(ChatSlice.Text));
            // ChatSliceMetadata fields
            var authorsField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.Authors));
            var chatEntriesField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.ChatEntries));
            var startOffsetField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.StartOffset));
            var endOffsetField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.EndOffset));
            var replyToEntriesField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.ReplyToEntries));
            var mentionsField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.Mentions));
            var reactionsField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.Reactions));
            var attachmentsField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.Attachments));
            var languageField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.Language));
            var contentTimestampField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.ContentTimestamp));
            var chatIdField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.ChatId));
            var placeIdField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.PlaceId));
            // ChatSliceEntry fields
            var chatSliceEntryIdField = namingPolicy.ConvertName(nameof(ChatSliceEntry.Id));
            var chatSliceEntryLocalIdField = namingPolicy.ConvertName(nameof(ChatSliceEntry.LocalId));
            var chatSliceEntryVersionField = namingPolicy.ConvertName(nameof(ChatSliceEntry.Version));
            // ChatSliceAttachment fields
            var attachmentIdField = namingPolicy.ConvertName(nameof(ChatSliceAttachment.Id));
            var attachmentSummaryField = namingPolicy.ConvertName(nameof(ChatSliceAttachment.Summary));

            var searchIndexResult = await openSearch.RunAsync(
                $$"""
                PUT /{{indexName}}
                {
                    "settings": {
                        "index.knn": true,
                        "default_pipeline": "{{ingestPipelineName}}"
                    },
                    "mappings": {
                        "_source": {
                            "excludes": [
                                "event_dense_embedding"
                            ]
                        },
                        "properties": {
                            "{{ChatInfoToChatSliceRelation.Name}}": {
                                "type": "join",
                                "relations": {
                                    "{{ChatInfoToChatSliceRelation.ChatInfoName}}": "{{ChatInfoToChatSliceRelation.ChatSliceName}}"
                                }
                            },
                            "{{TimestampFieldName}}": { "type": "date" },

                            "{{isPublicField}}": { "type": "boolean" },
                            "{{isBotChatField}}": { "type": "boolean" },

                            "{{metadataField}}": {
                                "type": "object",
                                "properties": {
                                    "{{authorsField}}": { "type": "keyword" },
                                    "{{chatIdField}}": { "type": "keyword" },
                                    "{{placeIdField}}": { "type": "keyword" },
                                    "{{chatEntriesField}}": {
                                        "type": "object",
                                        "properties": {
                                            "{{chatSliceEntryIdField}}":  { "type": "keyword" },
                                            "{{chatSliceEntryLocalIdField}}": { "type": "long" },
                                            "{{chatSliceEntryVersionField}}": { "type": "long" }
                                        }
                                    },
                                    "{{startOffsetField}}": { "type": "integer" },
                                    "{{endOffsetField}}": { "type": "integer" },
                                    "{{replyToEntriesField}}": { "type": "keyword" },
                                    "{{mentionsField}}": { "type": "keyword" },
                                    "{{reactionsField}}": { "type": "keyword" },
                                    "{{attachmentsField}}": {
                                        "type": "nested",
                                        "properties": {
                                            "{{attachmentIdField}}":  { "type": "keyword" },
                                            "{{attachmentSummaryField}}": { "type": "text" }
                                        }
                                    },
                                    "{{languageField}}": { "type": "keyword" },
                                    "{{contentTimestampField}}": { "type": "date" }
                                }
                            },
                            "{{textField}}": {
                                "type": "text"
                            },
                            "event_dense_embedding": {
                                "type": "knn_vector",
                                "dimension": {{modelDimension.ToString("D", CultureInfo.InvariantCulture)}},
                                "method": {
                                    "engine": "lucene",
                                    "space_type": "l2",
                                    "name": "hnsw",
                                    "parameters": {}
                                }
                            }
                        }
                    }
                }
                """,
                cancellationToken
            ).ConfigureAwait(false);
            searchIndexResult.AssertSuccess();
        }
    }

    public async Task EnsureChatsCursorIndexAsync(string chatsCursorIndexId, CancellationToken cancellationToken)
    {
        var isChatsCursorIndexExists = await IsIndexExistsAsync(chatsCursorIndexId, cancellationToken).ConfigureAwait(false);
        if (!isChatsCursorIndexExists) {
            // Chats cursor fields
            var lastVersionField = namingPolicy.ConvertName(nameof(ChatIndexInitializerShard.Cursor.LastVersion));

            var chatsCursorIndexResult = await openSearch.RunAsync(
                $$"""
                PUT /{{chatsCursorIndexId}}
                {
                    "mappings": {
                        "properties": {
                            "{{lastVersionField}}": {
                                "type": "text"
                            }
                        }
                    }
                }
                """,
                cancellationToken
            ).ConfigureAwait(false);
            chatsCursorIndexResult.AssertSuccess();
        }
    }

    public async Task EnsureContentCursorIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        var isIngestCursorIndexExists = await IsIndexExistsAsync(indexName, cancellationToken).ConfigureAwait(false);
        if (!isIngestCursorIndexExists) {
            // Cursor fields
            var lastEntryVersionField = namingPolicy.ConvertName(nameof(ChatContentCursor.LastEntryVersion));
            var lastEntryLocalIdField = namingPolicy.ConvertName(nameof(ChatContentCursor.LastEntryLocalId));

            // Note: https://opensearch.org/docs/latest/api-reference/index-apis/put-mapping/
            /*
            If you want to create or add mappings and fields to an index, you can use the put mapping
            API operation. For an existing mapping, this operation updates the mapping.

            You can’t use this operation to update mappings that already map to existing data in the index.
            You must first create a new index with your desired mappings, and then use the reindex API
            operation to map all the documents from your old index to the new index. If you don’t want
            any downtime while you re-index your indexes, you can use aliases.
            */
            var ingestCursorIndexResult = await openSearch.RunAsync(
                $$"""
                PUT /{{indexName}}
                {
                    "mappings": {
                        "properties": {
                            "{{lastEntryVersionField}}": {
                                "type": "text"
                            },
                            "{{lastEntryLocalIdField}}": {
                                "type": "text"
                            }
                        }
                    }
                }
                """,
                cancellationToken
            ).ConfigureAwait(false);
            ingestCursorIndexResult.AssertSuccess();
        }
    }

    public async Task EnsureEmbeddingIngestPipelineAsync(string pipelineName, string modelId, string textField, CancellationToken cancellationToken)
    {
        var isIngestPipelineExists = await IsPipelineExistsAsync(pipelineName, cancellationToken).ConfigureAwait(false);
        if (!isIngestPipelineExists) {
            textField = namingPolicy.ConvertName(textField);

            var ingestResult = await openSearch.RunAsync(
                $$"""
                PUT /_ingest/pipeline/{{pipelineName}}
                {
                    "description": "Autogenerated pipeline",
                    "processors": [
                        {
                            "script": {
                                "lang": "painless",
                                "source": "ctx.{{TimestampFieldName}} = new Date();"
                            }
                        },
                        {
                            "text_embedding": {
                                "model_id": "{{modelId}}",
                                "field_map": {
                                    "{{textField}}": "event_dense_embedding"
                                }
                            }
                        }
                    ]
                }
                """,
                cancellationToken
            ).ConfigureAwait(false);
            ingestResult.AssertSuccess();
        }
    }

    public async Task<bool> IsTemplateValidAsync(string templateName, string pattern, int? numberOfReplicas, CancellationToken cancellationToken)
    {
        var result = await openSearch.Indices.GetTemplateAsync(templateName, ct: cancellationToken).ConfigureAwait(false);
        result.AssertSuccess(allowNotFound: true);
        return result.TemplateMappings.TryGetValue(templateName, out var mapping)
            && mapping.IndexPatterns.Contains(pattern, StringComparer.Ordinal)
            && mapping.Settings.NumberOfReplicas == numberOfReplicas;
    }

    public async Task<bool> IsPipelineExistsAsync(string pipelineName, CancellationToken cancellationToken)
    {
        var isIngestPipelineExistsResult = await openSearch
            .Ingest
            .GetPipelineAsync(
                r => r.Id(pipelineName),
                ct: cancellationToken
            )
            .ConfigureAwait(false);
        isIngestPipelineExistsResult.AssertSuccess(allowNotFound: true);
        return isIngestPipelineExistsResult.Pipelines.ContainsKey(pipelineName);
    }

    public async Task<bool> IsIndexExistsAsync(string indexName, CancellationToken cancellationToken)
    {
        var isSearchIndexExistsResult = await openSearch
            .Indices
            .ExistsAsync(indexName, ct: cancellationToken)
            .ConfigureAwait(false);
        isSearchIndexExistsResult.AssertSuccess(allowNotFound: true);
        return isSearchIndexExistsResult.Exists;
    }
}

internal sealed class BuiltInModelClusterSetupActions : ClusterSetupActions
{
    private static readonly Encoding _utf8Encoding = new UTF8Encoding(false);

    private readonly IOpenSearchClient _openSearch;

    public BuiltInModelClusterSetupActions(
        IOpenSearchClient openSearch,
        OpenSearchNamingPolicy namingPolicy,
        Tracer baseTracer
    ) : base(openSearch, namingPolicy, baseTracer) => _openSearch = openSearch;

    protected override async Task<EmbeddingModelProps> RetrieveEmbeddingModelPropsAsync(OpenSearchSettings openSearchSettings, CancellationToken cancellationToken)
    {
        using var _1 = _tracer.Region();

        var modelGroupId = await GetModelGroupId(openSearchSettings, cancellationToken).ConfigureAwait(false);

        // Read model group latest model id
        var modelResponse = await _openSearch.RunAsync(
                $$"""
                POST /_plugins/_ml/models/_search
                {
                    "query": {
                        "bool": {
                            "must": [{
                                "exists": { "field": "model_config" },
                                "exists": { "field": "model_content_hash_value" },
                                "exists": { "field": "model_state" },
                                "match": {
                                    "model_group_id": "{{modelGroupId}}"
                                }
                            }]
                        }
                    },
                    "sort": [{
                        "_seq_no": { "order": "desc" }
                    }],
                    "size": 1
                }
                """,
                cancellationToken
            )
            .ConfigureAwait(false);
        var model = modelResponse
            .AssertSuccess()
            .FirstHit();
        var modelId = model.Get<string>("_id");
        if (modelId.IsNullOrEmpty()) {
            throw new InvalidOperationException(
                "Failed to retrieve model id."
            );
        }
        var modelSource = model.Get<IDictionary<string, object>>("_source")
            ?? throw new InvalidOperationException(
                "_source is null"
            );

        var modelConfig = modelSource.Get<IDictionary<string, object>>("model_config")
            ?? throw new InvalidOperationException(
                "model_config is null"
            );
        var modelEmbeddingDimension = Convert.ToInt32(
            modelConfig.Get<long>("embedding_dimension", int.MinValue)
        );
        if (modelEmbeddingDimension == int.MinValue) {
            throw new InvalidOperationException(
                "Failed to retrieve model embedding dimension value."
            );
        }
        // Get configs of the deployed model.
        var modelAllConfig = modelConfig.Get<string>("all_config");
        if (modelAllConfig.IsNullOrEmpty()) {
            throw new InvalidOperationException(
                "Failed to retrieve model all_config value."
            );
        }

#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
        var uniqueModelKey = Convert.ToHexString(SHA1.HashData(_utf8Encoding.GetBytes(modelAllConfig)))
            .ToLower(CultureInfo.InvariantCulture);
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

        return new EmbeddingModelProps(modelId, modelEmbeddingDimension, uniqueModelKey);
    }
}

internal sealed class CustomRemoteModelClusterSetupActions : ClusterSetupActions
{
    private readonly IOpenSearchClient _openSearch;

    public CustomRemoteModelClusterSetupActions(
        IOpenSearchClient openSearch,
        OpenSearchNamingPolicy namingPolicy,
        Tracer baseTracer
    ) : base(openSearch, namingPolicy, baseTracer) => _openSearch = openSearch;

    protected override async Task<EmbeddingModelProps> RetrieveEmbeddingModelPropsAsync(OpenSearchSettings openSearchSettings, CancellationToken cancellationToken)
    {
        var modelGroupId = await GetModelGroupId(openSearchSettings, cancellationToken).ConfigureAwait(false);

        throw new NotImplementedException();
    }

    private async ValueTask<EmbeddingModelProps> CreateEmbeddingModelPropertiesAsync(
        string modelId, IDictionary<string, object> modelSource, CancellationToken cancellationToken)
    {
        var connectorId = modelSource.Get<string>("connector_id")
            ?? throw new InvalidOperationException(
                "connector_id is null"
            );

        var connectorResponse = (await _openSearch
                .RunAsync($"GET /_plugins/_ml/connectors/{connectorId}", cancellationToken)
                .ConfigureAwait(false)
            ).AssertSuccess();

        var description = connectorResponse.Get<string>("description");
        if (description.IsNullOrEmpty()) {
            throw new InvalidOperationException(
                "Failed to retrieve connector description."
            );
        }

        var parts = description.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) {
            throw new InvalidOperationException(
                "Unexpected connector description format. That should be in the form '{EmbeddingDimension} {ModelContentHash}'."
            );
        }
        var embeddingDimension = int.Parse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture);
        var modelContentHash = parts[2];

        return new EmbeddingModelProps(modelId, embeddingDimension, modelContentHash);
    }
}
