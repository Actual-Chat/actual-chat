using ActualChat.MLSearch.ApiAdapters;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal class OpenSearchEngine(IOpenSearchClient openSearch, OpenSearchClusterSettings settings, ILoggerSource loggerSource)
    : ISearchEngine
{
    private ILogger? _log;
    private ILogger Log => _log ??= loggerSource.GetLogger(GetType());

    public async Task<SearchResult<TDocument>> Find<TDocument>(SearchQuery query, CancellationToken cancellationToken)
        where TDocument : class
    {
        // TODO: index id must be a parameter
        var queryBuilder = new OpenSearchQueryBuilder(settings, "chat-slice");
        var searchRequest = queryBuilder.Build(query);

        // TODO: Make this serialization optional
        var json = await searchRequest.ToJsonAsync(openSearch, cancellationToken).ConfigureAwait(false);

        Log.LogInformation(json);

        var response = await openSearch.SearchAsync<TDocument>(searchRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsValid) {
            throw new InvalidOperationException(response.DebugInformation, response.OriginalException);
        }

        var documents = (
                from hit in response.Hits
                select new RankedDocument<TDocument>(hit.Score, hit.Source)
            ).ToList().AsReadOnly();

        return new SearchResult<TDocument>(documents);
    }

    public async Task Ingest<TDocument>(TDocument document, CancellationToken cancellationToken)
        where TDocument : class
    {
        // TODO: support bulk api
        // TODO: index id must be a parameter
        var response = await openSearch.IndexAsync(
                document,
                e => e.Index(settings.IntoFullSearchIndexName("chat-slice")),
                cancellationToken
            )
            .ConfigureAwait(true);

        if (!response.IsValid) {
            throw new InvalidOperationException(response.DebugInformation, response.OriginalException);
        }
    }
}

internal static class OpenSearchExtensions
{
    public static async Task<string> ToJsonAsync(this ISearchRequest searchRequest, IOpenSearchClient openSearch, CancellationToken cancellationToken)
    {
        var serializableRequest = PostData.Serializable(searchRequest);
        serializableRequest.DisableDirectStreaming = false;

        var ms = new MemoryStream();
        await serializableRequest.WriteAsync(ms, openSearch.ConnectionSettings, cancellationToken).ConfigureAwait(false);
        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ms);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
