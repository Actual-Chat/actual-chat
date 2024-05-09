using ActualChat.Mesh;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.MLSearch.Indexing.Initializer;
using ActualChat.MLSearch.Module;
using Microsoft.Extensions.Options;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch.Setup;

internal interface IClusterSetup
{
    ClusterSettings Result { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
}

internal sealed class ClusterSetup(
    IOpenSearchClient openSearch,
    IMeshLocks meshLocks,
    IEnumerable<ISettingsChangeTokenSource> changeSources,
    IOptions<OpenSearchSettings> openSearchSettings,
    OpenSearchNamingPolicy namingPolicy,
    IndexNames indexNames,
    ILogger<ClusterSetup> log,
    Tracer baseTracer
) : IClusterSetup
{
    private readonly Tracer _tracer = baseTracer[typeof(ClusterSetup)];
    private ClusterSettings? _result;

    public ClusterSettings Result => _result ?? throw new InvalidOperationException(
        "Initialization script was not called."
    );

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var clusterSettings = await RetrieveClusterSettingsAsync(cancellationToken).ConfigureAwait(false);

        var isClusterStateValid = await CheckClusterStateValidAsync(clusterSettings, cancellationToken)
            .ConfigureAwait(false);
        if (isClusterStateValid) {
            return;
        }

        var runOptions = RunLockedOptions.Default with { Log = log };
        await meshLocks.RunLocked(
                nameof(InitializeAsync),
                runOptions,
                ct => InitialiseUnsafeAsync(clusterSettings, ct),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task InitialiseUnsafeAsync(ClusterSettings clusterSettings, CancellationToken cancellationToken)
    {
        await EnsureTemplatesAsync(cancellationToken).ConfigureAwait(false);
        await EnsureIndexesAsync(clusterSettings, cancellationToken).ConfigureAwait(false);
        NotifyClusterSettingsChanges();
    }

    private async Task<bool> CheckClusterStateValidAsync(ClusterSettings clusterSettings, CancellationToken cancellationToken)
    {
        var numberOfReplicas = openSearchSettings.Value.DefaultNumberOfReplicas;

        var (templateName, pattern) = (IndexNames.MLTemplateName, IndexNames.MLIndexPattern);
        var ingestPipelineName = indexNames.GetFullIngestPipelineName(IndexNames.ChatContent, clusterSettings);
        var indexShortNames = new[] { IndexNames.ChatContent, IndexNames.ChatContentCursor, IndexNames.ChatCursor };

        var isTemplateValid = await IsTemplateValidAsync(templateName, numberOfReplicas, pattern, cancellationToken)
            .ConfigureAwait(false);
        var isIngestPipelineExists = await IsPipelineExistsAsync(ingestPipelineName, cancellationToken)
            .ConfigureAwait(false);
        var isAllIndexesExist = true;
        foreach (var name in indexShortNames) {
            var fullIndexName = indexNames.GetFullName(name, clusterSettings);
            isAllIndexesExist &= await IsIndexExistsAsync(fullIndexName, cancellationToken).ConfigureAwait(false);
        }
        return isTemplateValid && isIngestPipelineExists && isAllIndexesExist;
    }

    private void NotifyClusterSettingsChanges()
    {
        foreach (var changeSource in changeSources) {
            changeSource.RaiseChanged();
        }
    }

    private async Task<ClusterSettings> RetrieveClusterSettingsAsync(CancellationToken cancellationToken)
    {
        if (_result != null)
            return _result;

        using var _1 = _tracer.Region();
        // Read model group latest state
        var modelGroupResponse = await openSearch.RunAsync(
                $$"""
                  POST /_plugins/_ml/model_groups/_search
                  {
                      "query": {
                          "match": {
                              "name": "{{openSearchSettings.Value.ModelGroup}}"
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
        var modelGroupId = modelGroupResponse.FirstHit().Get<string>("_id");
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

    private async Task EnsureTemplatesAsync(CancellationToken cancellationToken)
    {
        using var _ = _tracer.Region();

        var numberOfReplicas = openSearchSettings.Value.DefaultNumberOfReplicas;

        var (templateName, pattern) = (IndexNames.MLTemplateName, IndexNames.MLIndexPattern);
        var isValidTemplate = await IsTemplateValidAsync(templateName, numberOfReplicas, pattern, cancellationToken)
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

    private async Task<bool> IsTemplateValidAsync(string templateName, int? numberOfReplicas, string pattern, CancellationToken cancellationToken)
    {
        var result = await openSearch.Indices.GetTemplateAsync(templateName, ct: cancellationToken).ConfigureAwait(false);
        result.AssertSuccess(allowNotFound: true);
        return result.TemplateMappings.TryGetValue(templateName, out var mapping)
            && mapping.IndexPatterns.Contains(pattern, StringComparer.Ordinal)
            && mapping.Settings.NumberOfReplicas == numberOfReplicas;
    }

    private async Task EnsureIndexesAsync(ClusterSettings settings, CancellationToken cancellationToken)
    {
        // Notes:
        // Assumption: This is a script.
        // There's no reason make this script efficient.
        // It must fail and retried on any error.
        // It has to succeed once and only once to set up an OpenSearch cluster.
        // After the initial setup this would never be called again.
        using var _1 = _tracer.Region();
        var searchIndexId = indexNames.GetFullName(IndexNames.ChatContent, settings);
        var ingestCursorIndexId = indexNames.GetFullName(IndexNames.ChatContentCursor, settings);
        var chatsCursorIndexId = indexNames.GetFullName(IndexNames.ChatCursor, settings);

        var ingestPipelineId = indexNames.GetFullIngestPipelineName(IndexNames.ChatContent, settings);

        var modelId = settings.ModelId;
        var modelDimension = settings.ModelEmbeddingDimension.ToString("D", CultureInfo.InvariantCulture);

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
        var isPublicField = namingPolicy.ConvertName(nameof(ChatSliceMetadata.IsPublic));
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
        // Cursor fields
        var lastEntryVersionField = namingPolicy.ConvertName(nameof(ChatContentCursor.LastEntryVersion));
        var lastEntryLocalIdField = namingPolicy.ConvertName(nameof(ChatContentCursor.LastEntryLocalId));
        // Chats cursor fields
        var lastVersionField = namingPolicy.ConvertName(nameof(ChatIndexInitializerShard.Cursor.LastVersion));

        var isIngestPipelineExists = await IsPipelineExistsAsync(ingestPipelineId, cancellationToken).ConfigureAwait(false);
        if (!isIngestPipelineExists) {
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
                    $"Failed to update '{IndexNames.ChatContent}' ingest pipeline",
                    ingestResult.OriginalException
                );
            }
        }

        var isIngestCursorIndexExists = await IsIndexExistsAsync(ingestCursorIndexId, cancellationToken).ConfigureAwait(false);
        if (!isIngestCursorIndexExists) {
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
                    "Failed to update ingest cursor index.",
                    ingestCursorIndexResult.OriginalException
                );
            }
        }

        var isChatsCursorIndexExists = await IsIndexExistsAsync(chatsCursorIndexId, cancellationToken).ConfigureAwait(false);
        if (!isChatsCursorIndexExists) {
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
            if (!chatsCursorIndexResult.Success) {
                throw new InvalidOperationException(
                    "Failed to update chats cursor index",
                    chatsCursorIndexResult.OriginalException
                );
            }
        }

        var isSearchIndexExists = await IsIndexExistsAsync(searchIndexId, cancellationToken).ConfigureAwait(false);
        if (!isSearchIndexExists) {
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
                    $"Failed to update '{IndexNames.ChatContent}'search index",
                    searchIndexResult.OriginalException
                );
            }
        }
    }

    private async Task<bool> IsPipelineExistsAsync(string pipelineId, CancellationToken cancellationToken)
    {
        var isIngestPipelineExistsResult = await openSearch
            .Ingest
            .GetPipelineAsync(
                r => r.Id(pipelineId),
                ct: cancellationToken
            )
            .ConfigureAwait(false);
        isIngestPipelineExistsResult.AssertSuccess(allowNotFound: true);
        return isIngestPipelineExistsResult.Pipelines.Any();
    }

    private async Task<bool> IsIndexExistsAsync(string indexId, CancellationToken cancellationToken)
    {
        var isSearchIndexExistsResult = await openSearch
            .Indices
            .ExistsAsync(indexId, ct: cancellationToken)
            .ConfigureAwait(false);
        isSearchIndexExistsResult.AssertSuccess(allowNotFound: true);
        return isSearchIndexExistsResult.Exists;
    }
}
