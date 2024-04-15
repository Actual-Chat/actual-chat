using ActualChat.Mesh;
using ActualChat.Search.Db;
using ActualChat.Search.Module;
using OpenSearch.Client;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
namespace ActualChat.Search;

public sealed class OpenSearchConfigurator(IServiceProvider services) : WorkerBase
{
    private readonly TaskCompletionSource _whenCompleted = new ();
    private SearchSettings? _settings;
    private OpenSearchNames? _elasticNames;
    private IOpenSearchClient? _openSearchClient;
    private ILogger? _log;

    private SearchSettings Settings => _settings ??= services.GetRequiredService<SearchSettings>();
    private OpenSearchNames OpenSearchNames => _elasticNames ??= services.GetRequiredService<OpenSearchNames>();
    private IOpenSearchClient OpenSearchClient => _openSearchClient ??= services.GetRequiredService<IOpenSearchClient>();
    private IMeshLocks MeshLocks { get; }
        = services.GetRequiredService<IMeshLocks<SearchDbContext>>().WithKeyPrefix(nameof(OpenSearchConfigurator));
    private ILogger Log => _log ??= services.LogFor(GetType());

    public Task WhenCompleted => _whenCompleted.Task;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        if (!Settings.IsSearchEnabled) {
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

    private Task Run(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(EnsureEntryIndexTemplate),
            AsyncChain.From(EnsureContactIndices),
        };

        return (from chain in baseChains
            select chain
                .Retry(RetryDelaySeq.Exp(0, 60), 10)
                .Log(LogLevel.Debug, Log))
            .RunIsolated(cancellationToken);
    }

    private Task EnsureEntryIndexTemplate(CancellationToken cancellationToken)
    {
        var runOptions = RunLockedOptions.Default with { Log = Log };
        return MeshLocks.RunLocked(
            nameof(EnsureEntryIndexTemplate),
            runOptions,
            EnsureEntryIndexTemplateUnsafe,
            cancellationToken);
    }

    private Task EnsureContactIndices(CancellationToken cancellationToken)
    {
        var runOptions = RunLockedOptions.Default with { Log = Log };
        return MeshLocks.RunLocked(
            nameof(EnsureContactIndices),
            runOptions,
            EnsureContactIndicesUnsafe,
            cancellationToken);
    }

    private async Task EnsureEntryIndexTemplateUnsafe(CancellationToken cancellationToken)
    {
        var existsIndexTemplateResponse = await OpenSearchClient.Indices
            .TemplateExistsAsync(OpenSearchNames.EntryIndexTemplateName, ct: cancellationToken)
            .ConfigureAwait(false);
        if (existsIndexTemplateResponse.Exists)
            return;

        await OpenSearchClient.Indices
            .PutTemplateAsync(OpenSearchNames.EntryIndexTemplateName, ConfigureEntryIndexTemplate, cancellationToken)
            .Assert(Log)
            .ConfigureAwait(false);
    }

    private Task EnsureContactIndicesUnsafe(CancellationToken cancellationToken)
        => Task.WhenAll(
            EnsureContactIndex<IndexedUserContact>(OpenSearchNames.PublicUserIndexName,
                ConfigureUserContactIndex,
                cancellationToken),
            EnsureContactIndex<IndexedChatContact>(OpenSearchNames.GetChatContactIndexName(true),
                ConfigureChatContactIndex,
                cancellationToken),
            EnsureContactIndex<IndexedChatContact>(OpenSearchNames.GetChatContactIndexName(false),
                ConfigureChatContactIndex,
                cancellationToken));

    private async Task EnsureContactIndex<T>(IndexName indexName, Func<CreateIndexDescriptor, ICreateIndexRequest> configure, CancellationToken cancellationToken)
    {
        var existsResponse = await OpenSearchClient.Indices.ExistsAsync(indexName, ct:cancellationToken).ConfigureAwait(false);
        if (existsResponse.Exists)
            return;

        await OpenSearchClient.Indices
            .CreateAsync(indexName, configure, cancellationToken)
            .Assert(Log)
            .ConfigureAwait(false);
    }

    private IPutIndexTemplateRequest ConfigureEntryIndexTemplate(PutIndexTemplateDescriptor index)
        => index.Map<IndexedEntry>(m
            => m.Properties(p
                => p.Keyword(x => x.Name(e => e.Id))
                    .Text(x => x.Name(e => e.Content))
                    .Keyword(x => x.Name(e => e.ChatId))))
            .Settings(s => s.RefreshInterval(Settings.ElasticRefreshInterval))
            .IndexPatterns(OpenSearchNames.EntryIndexPattern);

    private ICreateIndexRequest ConfigureUserContactIndex(CreateIndexDescriptor index)
        => index.Map<IndexedUserContact>(m
            => m.Properties(p
                => p.Keyword(x => x.Name(e => e.Id))
                    .Text(x => x.Name(e => e.FullName))
                    .Text(x => x.Name(e => e.FirstName))
                    .Text(x => x.Name(e => e.SecondName))))
            .Settings(s => s.RefreshInterval(Settings.ElasticRefreshInterval));

    private ICreateIndexRequest ConfigureChatContactIndex(CreateIndexDescriptor index)
        => index.Map<IndexedChatContact>(m
            => m.Properties(p
                => p.Keyword(x => x.Name(e => e.Id))
                    .Keyword(x => x.Name(e => e.PlaceId))
                    .Text(x => x.Name(e => e.Title))))
            .Settings(s => s.RefreshInterval(Settings.ElasticRefreshInterval));
}
