using ActualChat.Redis;
using ActualChat.Search.Db;
using ActualChat.Search.Module;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;

namespace ActualChat.Search;

public class ElasticConfigurator(IServiceProvider services) : WorkerBase
{
    private readonly TaskCompletionSource _whenCompleted = new ();
    private SearchSettings? _settings;
    private ElasticsearchClient? _elastic;
    private DistributedLocks<SearchDbContext>? _distributedLock;
    private ILogger? _log;

    private SearchSettings Settings => _settings ??= services.GetRequiredService<SearchSettings>();
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

        try
        {
            await AsyncChain.From(EnsureIndexTemplate)
                .Retry(RetryDelaySeq.Exp(0, 60), 10)
                .Run(cancellationToken)
                .ConfigureAwait(false);
            _whenCompleted.SetResult();
        }
        catch (Exception e)
        {
            _whenCompleted.SetException(e);
            throw;
        }
    }

    private Task EnsureIndexTemplate(CancellationToken cancellationToken)
        => DistributedLocks.Run(EnsureIndexTemplateUnsafe, "EnsureIndexTemplate", cancellationToken);

    private async Task EnsureIndexTemplateUnsafe(CancellationToken cancellationToken)
    {
        var existsIndexTemplateResponse = await Elastic.Indices.ExistsIndexTemplateAsync(ElasticExt.IndexTemplateName, cancellationToken)
            .ConfigureAwait(false);
        if (existsIndexTemplateResponse.Exists)
            return;

        await Elastic.Indices
            .PutIndexTemplateAsync<IndexedEntry>(ElasticExt.IndexTemplateName, ConfigureEntryIndexTemplate, cancellationToken)
            .Assert(Log)
            .ConfigureAwait(false);
    }

    private static void ConfigureEntryIndexTemplate(PutIndexTemplateRequestDescriptor<IndexedEntry> index)
        => index.Template(ConfigureMappings).IndexPatterns(ElasticExt.IndexPattern);

    private static void ConfigureMappings(IndexTemplateMappingDescriptor<IndexedEntry> descriptor)
        => descriptor.Settings(s => s.RefreshInterval(Duration.MinusOne))
            .Mappings(m => m.Properties(ConfigureProperties));

    private static void ConfigureProperties(PropertiesDescriptor<IndexedEntry> p)
        => p.Keyword(x => x.Id).Text(x => x.Content).Keyword(x => x.ChatId);
}
