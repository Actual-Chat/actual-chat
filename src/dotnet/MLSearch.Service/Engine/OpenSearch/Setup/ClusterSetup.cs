using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using OpenSearch.Client;
using ActualChat.Hosting;
using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.Indexing;
using OpenSearchModelGroupName = string;
using OpenSearchModelGroupId = string;
using OpenSearchModelId = string;
using ActualChat.MLSearch.Indexing;

namespace ActualChat.MLSearch.Engine.OpenSearch.Setup;

internal class ClusterSetup(
    OpenSearchModelGroupName modelGroupName,
    IOpenSearchClient openSearch,
    ILogger<ClusterSetup> log,
    ITracerSource? tracing
    ) : IModuleInitializer
{
    private ClusterSettings? _result;
    public ClusterSettings Result => _result ?? throw new InvalidOperationException(
        "Initialization script was not called."
    );

    public Task Initialize(CancellationToken cancellationToken) => EnsureChatSliceIndex(cancellationToken);

    private async Task<ClusterSettings> RetrieveClusterSettingsAsync(CancellationToken cancellationToken)
    {
        if (_result != null) {
            return _result;
        }

        using var _1 = tracing.TraceRegion();
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
        var modelGroupId = modelGroupResponse.FirstHit().Get<OpenSearchModelGroupId>("_id");
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
        var model = modelResponse.FirstHit();
        var modelId = model.Get<OpenSearchModelId>("_id");
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
        var modelState = modelSource.Get<string>("model_state");
        if (!string.Equals(modelState, "DEPLOYED", StringComparison.Ordinal)) {
            throw new InvalidOperationException(
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
        return _result = new ClusterSettings(modelAllConfig, modelId, modelEmbeddingDimension);
    }
    private async Task EnsureChatSliceIndex(CancellationToken cancellationToken)
    {
        // Notes:
        // Assumption: This is a script.
        // There's no reason make this script efficient.
        // It must fail and retried on any error.
        // It has to succeed once and only once to setup an OpenSearch cluster.
        // After the initial setup this would never be called again.
        using var _1 = tracing.TraceRegion();
        var settings = await RetrieveClusterSettingsAsync(cancellationToken).ConfigureAwait(false);
        var searchIndexId = settings.IntoFullIndexName(IndexNames.ChatSlice);
        var ingestCursorIndexId = settings.IntoFullIndexName(IndexNames.ChatSliceCursor);

        var ingestPipelineId = settings.IntoFullIngestPipelineName(IndexNames.ChatSlice);
        var modelId = settings.ModelId;
        var modelDimension = settings.ModelEmbeddingDimension.ToString("D", CultureInfo.InvariantCulture);

        // Calculate field names
        var namingPolicy = JsonNamingPolicy.CamelCase;
        // ChatSlice fields
        var idField = namingPolicy.ConvertName(nameof(ChatSlice.Id));
        var metadataField = namingPolicy.ConvertName(nameof(ChatSlice.Metadata));
        var textField = namingPolicy.ConvertName(nameof(ChatSlice.Text));
        // ChatSliceMetadata fields
        var authorIdField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.AuthorId));
        var chatEntriesField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.ChatEntries));
        var startOffsetField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.StartOffset));
        var endOffsetField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.EndOffset));
        var replyToEntriesField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.ReplyToEntries));
        var mentionsField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.Mentions));
        var reactionsField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.Reactions));
        var participantsField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.ConversationParticipants));
        var attachmentsField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.Attachments));
        var isPublicField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.IsPublic));
        var languageField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.Language));
        var timestampField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.Timestamp));
        var chatIdField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.ChatId));
        var placeIdField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.PlaceId));
        // ChatSliceAttachment fields
        var attachmentIdField = namingPolicy.ConvertName(nameof(ChatSliceAttachment.Id));
        var attachmentSummaryField = namingPolicy.ConvertName(nameof(ChatSliceAttachment.Summary));
        // Cursor fields
        var lastEntryVersionField = namingPolicy.ConvertName(nameof(ChatHistoryExtractor.Cursor.LastEntryVersion));
        var lastEntryLocalIdField = namingPolicy.ConvertName(nameof(ChatHistoryExtractor.Cursor.LastEntryLocalId));

        var isIngestPipelineExistsResult = await openSearch
            .Ingest
            .GetPipelineAsync(
                r => r.Id(ingestPipelineId), 
                ct: cancellationToken
            )
            .ConfigureAwait(false);
        isIngestPipelineExistsResult.AssertSuccess(allowNotFound: true);
        if (!isIngestPipelineExistsResult.Pipelines.Any()) {
            var ingestResult = await openSearch.RunAsync(
                $$"""
                PUT /_ingest/pipeline/{{ingestPipelineId}}
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
            if (!ingestResult.Success) {
                throw new InvalidOperationException(
                    $"Failed to update '{IndexNames.ChatSlice}' ingest pipeline",
                    ingestResult.OriginalException
                );
            }
        }

        var isIngestCursorIndexExistsResult = await openSearch
            .Indices
            .ExistsAsync(ingestCursorIndexId, ct: cancellationToken)
            .ConfigureAwait(false);
        isIngestCursorIndexExistsResult.AssertSuccess(allowNotFound:true);
        if (!isIngestCursorIndexExistsResult.Exists) {
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
                PUT /{{ingestCursorIndexId}}
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
            if (!ingestCursorIndexResult.Success) {
                throw new InvalidOperationException(
                    "Failed to update search index",
                    ingestCursorIndexResult.OriginalException
                );
            }
        }

        var isSearchIndexExistsResult = await openSearch
            .Indices
            .ExistsAsync(searchIndexId, ct: cancellationToken)
            .ConfigureAwait(false);
        isSearchIndexExistsResult.AssertSuccess(allowNotFound:true);
        if (!isSearchIndexExistsResult.Exists) {
            var searchIndexResult = await openSearch.RunAsync(
                $$"""
                PUT /{{searchIndexId}}
                {
                    "settings": {
                        "index.knn": true,
                        "default_pipeline": "{{ingestPipelineId}}"
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
                                    "{{authorIdField}}": { "type": "keyword" },
                                    "{{chatIdField}}": { "type": "keyword" },
                                    "{{placeIdField}}": { "type": "keyword" },
                                    "{{chatEntriesField}}": { "type": "keyword" },
                                    "{{startOffsetField}}": { "type": "integer" },
                                    "{{endOffsetField}}": { "type": "integer" },
                                    "{{replyToEntriesField}}": { "type": "keyword" },
                                    "{{mentionsField}}": { "type": "keyword" },
                                    "{{reactionsField}}": { "type": "keyword" },
                                    "{{participantsField}}": { "type": "keyword" },
                                    "{{attachmentsField}}": {
                                        "type": "nested",
                                        "properties": {
                                            "{{attachmentIdField}}":  { "type": "keyword" },
                                            "{{attachmentSummaryField}}": { "type": "text" }
                                        }
                                    },
                                    "{{isPublicField}}": { "type": "boolean" },
                                    "{{languageField}}": { "type": "keyword" },
                                    "{{timestampField}}": { "type": "date" }
                                }
                            },
                            "{{textField}}": {
                                "type": "text"
                            },
                            "event_dense_embedding": {
                                "type": "knn_vector",
                                "dimension": {{modelDimension}},
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
            if (!searchIndexResult.Success) {
                throw new InvalidOperationException(
                    $"Failed to update '{IndexNames.ChatSlice}'search index",
                    searchIndexResult.OriginalException
                );
            }
        }
    }
}
