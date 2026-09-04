using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Module;
using OpenSearch.Client;
using IndexedEntry = ActualChat.MLSearch.Documents.IndexedEntry;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace ActualChat.MLSearch;

public sealed class OpenSearchConfigurator(IServiceProvider services) : WorkerBase
{
    private readonly TaskCompletionSource _whenReady = TaskCompletionSourceExt.New();

    private MLSearchSettings Settings => field ??= services.GetRequiredService<MLSearchSettings>();
    private OpenSearchNames OpenSearchNames => field ??= services.GetRequiredService<OpenSearchNames>();
    private IOpenSearchClient OpenSearchClient => field ??= services.GetRequiredService<IOpenSearchClient>();
    private IMeshLocks MeshLocks => field ??= services.MeshLocks().WithKeyPrefix(nameof(OpenSearchConfigurator));
    private ILogger Log => field ??= services.LogFor(GetType());

    private readonly int _numberOfReplicas = services.GetRequiredService<HostInfo>().IsDevelopmentInstance ? 0 : 1;

    public Task WhenReady => _whenReady.Task;

    /// <summary>
    /// Initializes OpenSearch indices. Can be called directly for testing
    /// or will be called automatically via hosted service.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_whenReady.Task.IsCompleted)
            return;

        if (!Settings.IsEnabled) {
            _whenReady.TrySetException(StandardError.Unavailable("Search feature is turned off."));
            return;
        }

        try {
            await Run(cancellationToken).ConfigureAwait(false);
            _whenReady.TrySetResult();
        }
        catch (Exception e) {
            _whenReady.TrySetException(e);
            throw;
        }
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => InitializeAsync(cancellationToken);

    private async Task Run(CancellationToken cancellationToken)
    {
        await RunChain(AsyncChain.From(EnsureTemplates)).ConfigureAwait(false);
        await RunChain(AsyncChain.From(EnsureIndices)).ConfigureAwait(false);

        return;

        Task RunChain(AsyncChain chain)
            => chain.Retry(RetryDelaySeq.Exp(1, 60), 10)
                .Log(LogLevel.Debug, Log)
                .RunIsolated(cancellationToken);
    }

    private Task EnsureTemplates(CancellationToken cancellationToken)
        => MeshLocks.LockAndRun(nameof(EnsureTemplates), EnsureTemplatesUnsafe, cancellationToken);

    private Task EnsureIndices(CancellationToken cancellationToken)
        => MeshLocks.LockAndRun(nameof(EnsureIndices), EnsureIndicesUnsafe, cancellationToken);

    private async Task EnsureTemplatesUnsafe(CancellationToken cancellationToken)
    {
        // Common template
        var commonTemplateExistsResponse = await OpenSearchClient.Indices
            .TemplateExistsAsync(OpenSearchNames.CommonIndexTemplateName, ct: cancellationToken)
            .ConfigureAwait(false);

        if (!commonTemplateExistsResponse.Exists)
            await OpenSearchClient.Indices
                .PutTemplateAsync(OpenSearchNames.CommonIndexTemplateName, ConfigureCommonIndexTemplate, cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
    }

    private Task EnsureIndicesUnsafe(CancellationToken cancellationToken)
        => Task.WhenAll(
            EnsureIndex(OpenSearchNames.UserIndexName,
                ConfigureUserIndex,
                cancellationToken),
            EnsureIndex(OpenSearchNames.GroupIndexName,
                ConfigureGroupContactIndex,
                cancellationToken),
            EnsureIndex(OpenSearchNames.PlaceIndexName,
                ConfigurePlaceContactIndex,
                cancellationToken),
            EnsureEntryIndex(cancellationToken));

    private async Task EnsureEntryIndex(CancellationToken cancellationToken)
    {
        await EnsureIndex(OpenSearchNames.EntryIndexName, ConfigureEntryIndex, cancellationToken).ConfigureAwait(false);
        // Hashtags and AuthorUserId were added after the index shipped, and EnsureIndex is create-only.
        // Merging their mappings here keeps the live index searchable without a version bump / reindex:
        // Hashtags must stay keyword (a tag with '-' would otherwise analyze into several terms), and
        // AuthorUserId backs the "my messages" filter. Existing docs get the field only once reindexed.
        await OpenSearchClient.Indices
            .PutMappingAsync<IndexedEntry>(m => m
                    .Index(OpenSearchNames.EntryIndexName)
                    .Properties(pp => pp
                        .Keyword(p => p.Name(x => x.Hashtags))
                        .Keyword(p => p.Name(x => x.AuthorUserId))),
                cancellationToken)
            .Assert(Log)
            .ConfigureAwait(false);
    }

    private async Task EnsureIndex(
        IndexName indexName,
        Func<CreateIndexDescriptor, ICreateIndexRequest> configure,
        CancellationToken cancellationToken)
    {
        var existsResponse = await OpenSearchClient.Indices
            .ExistsAsync(indexName, ct: cancellationToken)
            .ConfigureAwait(false);
        if (existsResponse.Exists)
            return;

        await OpenSearchClient.Indices
            .CreateAsync(indexName, GetLoggedCreateIndexRequest, cancellationToken)
            .Assert(Log)
            .ConfigureAwait(false);
        return;

        ICreateIndexRequest GetLoggedCreateIndexRequest(CreateIndexDescriptor d)
            => configure(d).Log(OpenSearchClient, Log, $"Ensure index '{indexName}' request");
    }

    private IPutIndexTemplateRequest ConfigureCommonIndexTemplate(PutIndexTemplateDescriptor index)
        => index
            .Settings(s => s
                .RefreshInterval(Settings.RefreshInterval)
                .NumberOfReplicas(_numberOfReplicas))
            .IndexPatterns(OpenSearchNames.CommonIndexPattern);

    private ICreateIndexRequest ConfigureUserIndex(CreateIndexDescriptor index)
        => index
            .Map<IndexedUser>(m
                => m.Properties(pp
                    => pp.Keyword(p => p.Name(x => x.Id))
                        .Text(p => p.Name(x => x.Name))
                        .Keyword(x => x.Name(o => o.PlaceIds))
                        .Join(j => j.Name(x => x.ContactToUser)
                            .Relations(r => r.Join<IndexedUser, IndexedUserContact>()))))
            .Map<IndexedUserContact>(m
                => m.RoutingField(r => r.Required())
                    .Properties(pp
                        => pp.Keyword(p => p.Name(x => x.Id))
                            .Keyword(p => p.Name(x => x.OwnerId))
                            .Keyword(p => p.Name(x => x.OtherUserId))
                            .Text(p => p.Name(x => x.Name))
                            .Text(p => p.Name(x => x.ExternalContactName))
                            .Join(j => j.Name(x => x.ContactToUser)
                                .Relations(r => r.Join<IndexedUser, IndexedUserContact>()))))
            .Settings(s => s.RefreshInterval(Settings.RefreshInterval));

    private ICreateIndexRequest ConfigureGroupContactIndex(CreateIndexDescriptor index)
        => index.Map<IndexedGroup>(m
            => m.Properties(pp
                => pp.Keyword(p => p.Name(x => x.Id))
                    .Keyword(p => p.Name(x => x.PlaceId))
                    .Text(p => p.Name(x => x.Title))))
            .Settings(s => s.RefreshInterval(Settings.RefreshInterval));

    private ICreateIndexRequest ConfigurePlaceContactIndex(CreateIndexDescriptor index)
        => index.Map<IndexedPlace>(m
            => m.Properties(pp
                => pp.Keyword(p => p.Name(x => x.Id))
                    .Boolean(p => p.Name(x => x.IsPublic))
                    .Text(p => p.Name(x => x.Title))))
            .Settings(s => s.RefreshInterval(Settings.RefreshInterval));

    private ICreateIndexRequest ConfigureEntryIndex(CreateIndexDescriptor index)
        => index
            .Map<IndexedChat>(m
                => m.Properties(pp
                    => pp.Keyword(p => p.Name(x => x.Id))
                        .Keyword(p => p.Name(x => x.PlaceId))
                        .Boolean(p => p.Name(x => x.IsPublic))
                        .Join(j => j.Name(x => x.EntryToChat).Relations(r => r.Join<IndexedChat, IndexedEntry>()))))
            .Map<IndexedEntry>(m
                => m.RoutingField(r => r.Required())
                    .Properties(pp
                    => pp.Keyword(p => p.Name(x => x.Id))
                        .Text(p => p.Name(e => e.Content))
                        .Keyword(p => p.Name(x => x.Hashtags))
                        .Date(p => p.Name(x => x.At))
                        .Keyword(p => p.Name(x => x.AuthorUserId))
                        .Join(j => j.Name(x => x.EntryToChat).Relations(r => r.Join<IndexedChat, IndexedEntry>())))
                )
            .Settings(s => s
                .RefreshInterval(Settings.RefreshInterval)
                .NumberOfReplicas(_numberOfReplicas));
}
