using ActualChat.Hosting;
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
    private IndexNames? _openSearchNames;
    private IOpenSearchClient? _openSearchClient;
    private ILogger? _log;

    private SearchSettings Settings => _settings ??= services.GetRequiredService<SearchSettings>();
    private IndexNames IndexNames => _openSearchNames ??= services.GetRequiredService<IndexNames>();
    private IOpenSearchClient OpenSearchClient => _openSearchClient ??= services.GetRequiredService<IOpenSearchClient>();
    private IMeshLocks MeshLocks { get; }
        = services.GetRequiredService<IMeshLocks<SearchDbContext>>().WithKeyPrefix(nameof(OpenSearchConfigurator));
    private ILogger Log => _log ??= services.LogFor(GetType());

    private readonly int _numberOfReplicas = services.GetRequiredService<HostInfo>().IsDevelopmentInstance ? 0 : 1;

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

    private async Task Run(CancellationToken cancellationToken)
    {
        await RunChain(AsyncChain.From(EnsureTemplates), cancellationToken).ConfigureAwait(false);
        await RunChain(AsyncChain.From(EnsureContactIndices), cancellationToken).ConfigureAwait(false);

        return;

        Task RunChain(AsyncChain chain, CancellationToken cancellationToken)
            => chain.Retry(RetryDelaySeq.Exp(0, 60), 10)
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

    private Task EnsureContactIndices(CancellationToken cancellationToken)
    {
        var runOptions = RunLockedOptions.Default with { Log = Log };
        return MeshLocks.RunLocked(
            nameof(EnsureContactIndices),
            runOptions,
            EnsureContactIndicesUnsafe,
            cancellationToken);
    }

    private async Task EnsureTemplatesUnsafe(CancellationToken cancellationToken)
    {
        // Common template
        var commonTemplateExistsResponse = await OpenSearchClient.Indices
            .TemplateExistsAsync(IndexNames.CommonIndexTemplateName, ct: cancellationToken)
            .ConfigureAwait(false);

        if (!commonTemplateExistsResponse.Exists) {
            await OpenSearchClient.Indices
                .PutTemplateAsync(IndexNames.CommonIndexTemplateName, ConfigureCommonIndexTemplate, cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        }

        // Entry index template
        var entryTemplateExistsResponse = await OpenSearchClient.Indices
            .TemplateExistsAsync(IndexNames.EntryIndexTemplateName, ct: cancellationToken)
            .ConfigureAwait(false);
        if (entryTemplateExistsResponse.Exists)
            return;

        await OpenSearchClient.Indices
            .PutTemplateAsync(IndexNames.EntryIndexTemplateName, ConfigureEntryIndexTemplate, cancellationToken)
            .Assert(Log)
            .ConfigureAwait(false);
    }

    private Task EnsureContactIndicesUnsafe(CancellationToken cancellationToken)
        => Task.WhenAll(
            EnsureContactIndex<IndexedUserContact>(IndexNames.PublicUserIndexName,
                ConfigureUserContactIndex,
                cancellationToken),
            EnsureContactIndex<IndexedChatContact>(IndexNames.GetChatContactIndexName(true),
                ConfigureChatContactIndex,
                cancellationToken),
            EnsureContactIndex<IndexedChatContact>(IndexNames.GetChatContactIndexName(false),
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

    private IPutIndexTemplateRequest ConfigureCommonIndexTemplate(PutIndexTemplateDescriptor index)
        => index
            .Settings(s => s
                .RefreshInterval(Settings.RefreshInterval)
                .NumberOfReplicas(_numberOfReplicas))
            .IndexPatterns(IndexNames.CommonIndexPattern);

    private IPutIndexTemplateRequest ConfigureEntryIndexTemplate(PutIndexTemplateDescriptor index)
        => index.Map<IndexedEntry>(m
            => m.Properties(p
                => p.Keyword(x => x.Name(e => e.Id))
                    .Text(x => x.Name(e => e.Content))
                    .Keyword(x => x.Name(e =>  e.ChatId))))
            .Settings(s => s
                .RefreshInterval(Settings.RefreshInterval)
                .NumberOfReplicas(_numberOfReplicas))
            .IndexPatterns(IndexNames.EntryIndexPattern);

    private ICreateIndexRequest ConfigureUserContactIndex(CreateIndexDescriptor index)
        => index.Map<IndexedUserContact>(m
            => m.Properties(p
                => p.Keyword(x => x.Name(e => e.Id))
                    .Text(x => x.Name(e => e.FullName))
                    .Text(x => x.Name(e => e.FirstName))
                    .Text(x => x.Name(e => e.SecondName))))
            .Settings(s => s.RefreshInterval(Settings.RefreshInterval));

    private ICreateIndexRequest ConfigureChatContactIndex(CreateIndexDescriptor index)
        => index.Map<IndexedChatContact>(m
            => m.Properties(p
                => p.Keyword(x => x.Name(e => e.Id))
                    .Keyword(x => x.Name(e => e.PlaceId))
                    .Text(x => x.Name(e => e.Title))))
            .Settings(s => s.RefreshInterval(Settings.RefreshInterval));
}
