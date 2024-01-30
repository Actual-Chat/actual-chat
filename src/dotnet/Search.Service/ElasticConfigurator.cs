using ActualChat.Redis;
using ActualChat.Search.Db;
using ActualChat.Search.Module;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;

namespace ActualChat.Search;

public class ElasticConfigurator(IServiceProvider services) : WorkerBase
{
    private readonly TaskCompletionSource _whenCompleted = new ();
    private SearchSettings? _settings;
    private ElasticNames? _elasticNames;
    private ElasticsearchClient? _elastic;
    private DistributedLocks<SearchDbContext>? _distributedLock;
    private ILogger? _log;

    private SearchSettings Settings => _settings ??= services.GetRequiredService<SearchSettings>();
    private ElasticNames ElasticNames => _elasticNames ??= services.GetRequiredService<ElasticNames>();
    private ElasticsearchClient Elastic => _elastic ??= services.GetRequiredService<ElasticsearchClient>();
    private DistributedLocks<SearchDbContext> DistributedLocks
        => _distributedLock ??= services.GetRequiredService<DistributedLocks<SearchDbContext>>();
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
        => DistributedLocks.Run(EnsureEntryIndexTemplateUnsafe, "EnsureEntryIndexTemplate", cancellationToken);

    private Task EnsureContactIndices(CancellationToken cancellationToken)
        => DistributedLocks.Run(EnsureContactIndicesUnsafe, "EnsureContactIndices", cancellationToken);

    private async Task EnsureEntryIndexTemplateUnsafe(CancellationToken cancellationToken)
    {
        var existsIndexTemplateResponse = await Elastic.Indices.ExistsIndexTemplateAsync(ElasticNames.EntryIndexTemplateName, cancellationToken)
            .ConfigureAwait(false);
        if (existsIndexTemplateResponse.Exists)
            return;

        await Elastic.Indices
            .PutIndexTemplateAsync<IndexedEntry>(ElasticNames.EntryIndexTemplateName, ConfigureEntryIndexTemplate, cancellationToken)
            .Assert(Log)
            .ConfigureAwait(false);
    }

    private Task EnsureContactIndicesUnsafe(CancellationToken cancellationToken)
        => Task.WhenAll(
            EnsureContactIndex<IndexedUserContact>(ElasticNames.PublicUserIndexName,
                ConfigureUserContactIndex,
                cancellationToken),
            EnsureContactIndex<IndexedChatContact>(ElasticNames.GetChatContactIndexName(true),
                ConfigureChatContactIndex,
                cancellationToken),
            EnsureContactIndex<IndexedChatContact>(ElasticNames.GetChatContactIndexName(false),
                ConfigureChatContactIndex,
                cancellationToken));

    private async Task EnsureContactIndex<T>(IndexName indexName, Action<CreateIndexRequestDescriptor<T>> configure, CancellationToken cancellationToken)
    {
        var existsResponse = await Elastic.Indices.ExistsAsync(indexName, cancellationToken).ConfigureAwait(false);
        if (existsResponse.Exists)
            return;

        await Elastic.Indices
            .CreateAsync(indexName, configure, cancellationToken)
            .Assert(Log)
            .ConfigureAwait(false);
    }

    private void ConfigureEntryIndexTemplate(PutIndexTemplateRequestDescriptor<IndexedEntry> index)
        => index.Template(t
                => t.Mappings(m
                        => m.Properties(p
                            => p.Keyword(x => x.Id)
                                .Text(x => x.Content)
                                .Keyword(x => x.ChatId)))
                    .Settings(s => s.RefreshInterval(Settings.ElasticRefreshInterval)))
            .IndexPatterns(ElasticNames.EntryIndexPattern);

    private void ConfigureUserContactIndex(CreateIndexRequestDescriptor<IndexedUserContact> index)
        => index.Mappings(m
                => m.Properties(p
                    => p.Keyword(x => x.Id)
                        .Text(x => x.FullName)
                        .Text(x => x.FirstName)
                        .Text(x => x.SecondName)))
            .Settings(s => s.RefreshInterval(Settings.ElasticRefreshInterval));

    private void ConfigureChatContactIndex(CreateIndexRequestDescriptor<IndexedChatContact> index)
        => index.Mappings(m
                => m.Properties(p
                    => p.Keyword(x => x.Id)
                        .Keyword(x => x.PlaceId)
                        .Text(x => x.Title)))
            .Settings(s => s.RefreshInterval(Settings.ElasticRefreshInterval));
}
