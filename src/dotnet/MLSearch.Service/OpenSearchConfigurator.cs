using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Module;
using ActualChat.Search;
using OpenSearch.Client;
using IndexedEntry = ActualChat.MLSearch.Documents.IndexedEntry;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
namespace ActualChat.MLSearch;

// TODO: merge with cluster setup actions or cluster setup
public sealed class OpenSearchConfigurator(IServiceProvider services) : WorkerBase
{
    private readonly TaskCompletionSource _whenCompleted = new ();
    private MLSearchSettings? _settings;
    private OpenSearchNames? _openSearchNames;
    private IOpenSearchClient? _openSearchClient;
    private ILogger? _log;

    private MLSearchSettings Settings => _settings ??= services.GetRequiredService<MLSearchSettings>();
    private OpenSearchNames OpenSearchNames => _openSearchNames ??= services.GetRequiredService<OpenSearchNames>();
    private IOpenSearchClient OpenSearchClient => _openSearchClient ??= services.GetRequiredService<IOpenSearchClient>();
    private IMeshLocks MeshLocks { get; }
        = services.GetRequiredService<IMeshLocks<MLSearchDbContext>>().WithKeyPrefix(nameof(OpenSearchConfigurator));
    private ILogger Log => _log ??= services.LogFor(GetType());

    private readonly int _numberOfReplicas = services.GetRequiredService<HostInfo>().IsDevelopmentInstance ? 0 : 1;

    public Task WhenCompleted => _whenCompleted.Task;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        if (!Settings.IsEnabled) {
            _whenCompleted.SetException(StandardError.Unavailable("Search feature is turned off."));
            return;
        }

        try {
            await Run(cancellationToken).ConfigureAwait(false);
            _whenCompleted.SetResult();
        }
        catch (Exception e)
        {
            _whenCompleted.SetException(e);
            throw;
        }
    }

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
    {
        var runOptions = RunLockedOptions.Default with { Log = Log };
        return MeshLocks.RunLocked(
            nameof(EnsureTemplates),
            runOptions,
            EnsureTemplatesUnsafe,
            cancellationToken);
    }

    private Task EnsureIndices(CancellationToken cancellationToken)
    {
        var runOptions = RunLockedOptions.Default with { Log = Log };
        return MeshLocks.RunLocked(
            nameof(EnsureIndices),
            runOptions,
            EnsureIndicesUnsafe,
            cancellationToken);
    }

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
                ConfigureUserContactIndex,
                cancellationToken),
            EnsureIndex(OpenSearchNames.GroupIndexName,
                ConfigureGroupContactIndex,
                cancellationToken),
            EnsureIndex(OpenSearchNames.PlaceIndexName,
                ConfigurePlaceContactIndex,
                cancellationToken),
            EnsureIndex(OpenSearchNames.EntryIndexName,
                ConfigureEntryIndex,
                cancellationToken));

    private async Task EnsureIndex(IndexName indexName, Func<CreateIndexDescriptor, ICreateIndexRequest> configure, CancellationToken cancellationToken)
    {
        var existsResponse = await OpenSearchClient.Indices.ExistsAsync(indexName, ct:cancellationToken).ConfigureAwait(false);
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

    private ICreateIndexRequest ConfigureUserContactIndex(CreateIndexDescriptor index)
        => index.Map<IndexedUserContact>(m
            => m.Properties(pp
                => pp.Keyword(p => p.Name(x => x.Id))
                    .Text(p => p.Name(x => x.FullName))
                    .Text(p => p.Name(x => x.FirstName))
                    .Text(p => p.Name(x => x.SecondName))
                    .Keyword(x => x.Name(o => o.PlaceIds))))
            .Settings(s => s.RefreshInterval(Settings.RefreshInterval));

    private ICreateIndexRequest ConfigureGroupContactIndex(CreateIndexDescriptor index)
        => index.Map<IndexedGroupChatContact>(m
            => m.Properties(pp
                => pp.Keyword(p => p.Name(x => x.Id))
                    .Keyword(p => p.Name(x => x.PlaceId))
                    .Text(p => p.Name(x => x.Title))))
            .Settings(s => s.RefreshInterval(Settings.RefreshInterval));

    private ICreateIndexRequest ConfigurePlaceContactIndex(CreateIndexDescriptor index)
        => index.Map<IndexedPlaceContact>(m
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
                        .Date(p => p.Name(x => x.At))
                        .Join(j => j.Name(x => x.EntryToChat).Relations(r => r.Join<IndexedChat, IndexedEntry>())))
                )
            .Settings(s => s
                .RefreshInterval(Settings.RefreshInterval)
                .NumberOfReplicas(_numberOfReplicas));
}
