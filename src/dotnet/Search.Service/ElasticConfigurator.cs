using ActualChat.Search.Module;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;

namespace ActualChat.Search;

public class ElasticConfigurator(IServiceProvider services) : WorkerBase
{
    private readonly TaskCompletionSource _whenCompleted = new ();
    private SearchSettings? _settings;
    private ElasticsearchClient? _elastic;
    private ILogger? _log;

    private SearchSettings Settings => _settings ??= services.GetRequiredService<SearchSettings>();
    private ElasticsearchClient Elastic => _elastic ??= services.GetRequiredService<ElasticsearchClient>();
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

    private async Task EnsureIndexTemplate(CancellationToken cancellationToken)
    {
        var existsIndexTemplateResponse = await Elastic.Indices.ExistsIndexTemplateAsync(ElasticExt.IndexTemplateName, cancellationToken)
            .ConfigureAwait(false);
        if (existsIndexTemplateResponse.Exists)
            return;

        await Elastic.Indices
            .PutIndexTemplateAsync<IndexedEntry>(ElasticExt.IndexTemplateName, EntryIndexTemplate, cancellationToken)
            .Assert(Log)
            .ConfigureAwait(false);
    }

    private static void EntryIndexTemplate(PutIndexTemplateRequestDescriptor<IndexedEntry> index)
        => index.Template(ConfigureMappings).IndexPatterns(ElasticExt.IndexPattern);

    private static void ConfigureMappings(IndexTemplateMappingDescriptor<IndexedEntry> descriptor)
        => descriptor.Mappings(m => m.Properties(p => p.LongNumber(x => x.Id).Text(x => x.Content)));
}
