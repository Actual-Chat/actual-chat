using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.MLSearch.Indexing.Initializer;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch.Setup;

internal interface IClusterSetupActions
{
    Task<EmbeddingModelProps> RetrieveEmbeddingModelPropsAsync(string modelGroup, CancellationToken cancellationToken);
    Task<bool> IsTemplateValidAsync(string templateName, string pattern, int? numberOfReplicas, CancellationToken cancellationToken);
    Task<bool> IsPipelineExistsAsync(string pipelineName, CancellationToken cancellationToken);
    Task<bool> IsIndexExistsAsync(string indexName, CancellationToken cancellationToken);
    Task EnsureTemplateAsync(string templateName, string pattern, int? numberOfReplicas, CancellationToken cancellationToken);
    Task EnsureEmbeddingIngestPipelineAsync(string pipelineName, string modelId, string textField, CancellationToken cancellationToken);
    Task EnsureContentIndexAsync(string indexName, string ingestPipelineName, int modelDimension, CancellationToken cancellationToken);
    Task EnsureContentCursorIndexAsync(string indexName, CancellationToken cancellationToken);
    Task EnsureChatsCursorIndexAsync(string indexName, CancellationToken cancellationToken);
}

internal sealed class ClusterSetupActions(
    IOpenSearchClient openSearch,
    OpenSearchNamingPolicy namingPolicy,
    Tracer baseTracer
) : IClusterSetupActions
{
     private readonly Tracer _tracer = baseTracer[typeof(ClusterSetup)];

    public async Task<EmbeddingModelProps> RetrieveEmbeddingModelPropsAsync(string modelGroupName, CancellationToken cancellationToken)
    {
        using var _1 = _tracer.Region();
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
        // Read model group latest model id
        var modelResponse = await openSearch.RunAsync(
                $$"""
                POST /_plugins/_ml/models/_search
                {
                    "query": {
                        "match": {
                            "model_group_id": "{{modelGroupId}}"
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

        // Ensure model is deployed.
        if (!modelSource.TryGetValue("model_state", out var modelStateObj)) {
            throw new InvalidOperationException("model_state field is not found");
        }
        var modelState = (string) modelStateObj;
        if (!string.Equals(modelState, "DEPLOYED", StringComparison.Ordinal)) {
            modelState = string.IsNullOrEmpty(modelState) ? "<Empty>" : modelState;
            // Throw standard external error as it is transient
            throw StandardError.External(
                $"Invalid model state. Expecting deployed model, but was {modelState}."
            );
        }
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
        return new EmbeddingModelProps(modelId, modelEmbeddingDimension, modelAllConfig);
    }

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
            // ChatSlice fields
            var idField = namingPolicy.ConvertName(nameof(ChatSlice.Id));
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
            var timestampField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.Timestamp));
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
                            "{{idField}}": {
                                "type": "keyword"
                            },
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
                                    "{{timestampField}}": { "type": "date" }
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
                            },
                            "{{ChatInfoToChatSliceRelation.Name}}": {
                                "type": "join",
                                "relations": {
                                    "{{ChatInfoToChatSliceRelation.ChatInfoName}}": "{{ChatInfoToChatSliceRelation.ChatSliceName}}"
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
                    "processors": [{
                        "text_embedding": {
                            "model_id": "{{modelId}}",
                            "field_map": {
                                "{{textField}}": "event_dense_embedding"
                            }
                        }
                    }]
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
