using ActualChat.Mesh;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Module;
using Microsoft.Extensions.Options;

namespace ActualChat.MLSearch.Engine.OpenSearch.Setup;

internal sealed record ClusterSetupResult(EmbeddingModelProps EmbeddingModelProps);

internal interface IClusterSetup
{
    ClusterSetupResult Result { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
}

internal sealed class ClusterSetup(
    IMeshLocks meshLocks,
    IClusterSetupActions actions,
    IOptions<OpenSearchSettings> openSearchSettings,
    IEnumerable<ISettingsChangeTokenSource> changeSources,
    ILogger<ClusterSetup> log,
    IndexNames indexNames,
    Tracer baseTracer
) : IClusterSetup
{
    private readonly Tracer _tracer = baseTracer[typeof(ClusterSetup)];
    private ClusterSetupResult? _result;

    public ClusterSetupResult Result => _result ?? throw new InvalidOperationException(
        "Initialization script was not called."
    );

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var modelGroup = openSearchSettings.Value.ModelGroup;
        var embeddingModelProps = await actions
            .RetrieveEmbeddingModelPropsAsync(modelGroup, cancellationToken)
            .ConfigureAwait(false);

        var isClusterStateValid = await CheckClusterStateValidAsync(embeddingModelProps, cancellationToken)
            .ConfigureAwait(false);
        if (isClusterStateValid) {
            _result = new ClusterSetupResult(embeddingModelProps);
            return;
        }

        var runOptions = RunLockedOptions.Default with { Log = log };
        await meshLocks.RunLocked(
                nameof(InitializeAsync),
                runOptions,
                ct => InitialiseUnsafeAsync(embeddingModelProps, ct),
                cancellationToken
            )
            .ConfigureAwait(false);

        _result = new ClusterSetupResult(embeddingModelProps);

        NotifyClusterSettingsChanges();
    }

    public async Task<bool> CheckClusterStateValidAsync(EmbeddingModelProps clusterSettings, CancellationToken cancellationToken)
    {
        var numberOfReplicas = openSearchSettings.Value.DefaultNumberOfReplicas;

        var (templateName, pattern) = (IndexNames.MLTemplateName, IndexNames.MLIndexPattern);
        var ingestPipelineName = indexNames.GetFullIngestPipelineName(IndexNames.ChatContent, clusterSettings);
        var indexShortNames = new[] { IndexNames.ChatContent, IndexNames.ChatContentCursor, IndexNames.ChatCursor };

        var isTemplateValid = await actions.IsTemplateValidAsync(templateName, pattern, numberOfReplicas, cancellationToken)
            .ConfigureAwait(false);
        var isIngestPipelineExists = await actions.IsPipelineExistsAsync(ingestPipelineName, cancellationToken)
            .ConfigureAwait(false);
        var isAllIndexesExist = true;
        foreach (var name in indexShortNames) {
            var fullIndexName = indexNames.GetFullName(name, clusterSettings);
            isAllIndexesExist &= await actions.IsIndexExistsAsync(fullIndexName, cancellationToken).ConfigureAwait(false);
        }
        return isTemplateValid && isIngestPipelineExists && isAllIndexesExist;
    }

    private async Task InitialiseUnsafeAsync(EmbeddingModelProps clusterSettings, CancellationToken cancellationToken)
    {
        await EnsureTemplatesAsync(cancellationToken).ConfigureAwait(false);
        await EnsureIndexesAsync(clusterSettings, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureTemplatesAsync(CancellationToken cancellationToken)
    {
        using var _ = _tracer.Region();

        await actions.EnsureTemplateAsync(
                IndexNames.MLTemplateName,
                IndexNames.MLIndexPattern,
                openSearchSettings.Value.DefaultNumberOfReplicas,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task EnsureIndexesAsync(EmbeddingModelProps settings, CancellationToken cancellationToken)
    {
        // Notes:
        // Assumption: This is a script.
        // There's no reason make this script efficient.
        // It must fail and retried on any error.
        // It has to succeed once and only once to set up an OpenSearch cluster.
        // After the initial setup this would never be called again.
        using var _1 = _tracer.Region();
        var contentIndexName = indexNames.GetFullName(IndexNames.ChatContent, settings);
        var contentCursorIndexName = indexNames.GetFullName(IndexNames.ChatContentCursor, settings);
        var chatsCursorIndexName = indexNames.GetFullName(IndexNames.ChatCursor, settings);

        var ingestPipelineName = indexNames.GetFullIngestPipelineName(IndexNames.ChatContent, settings);

        var modelId = settings.Id;

        await actions.EnsureChatsCursorIndexAsync(chatsCursorIndexName, cancellationToken).ConfigureAwait(false);

        await actions.EnsureEmbeddingIngestPipelineAsync(ingestPipelineName, modelId, nameof(ChatSlice.Text), cancellationToken)
            .ConfigureAwait(false);
        await actions.EnsureContentCursorIndexAsync(contentCursorIndexName, cancellationToken).ConfigureAwait(false);
        await actions.EnsureContentIndexAsync(
                contentIndexName,
                ingestPipelineName,
                settings.EmbeddingDimension,
                cancellationToken
            )
            .ConfigureAwait(false);
    }


    private void NotifyClusterSettingsChanges()
    {
        foreach (var changeSource in changeSources) {
            changeSource.RaiseChanged();
        }
    }
}
