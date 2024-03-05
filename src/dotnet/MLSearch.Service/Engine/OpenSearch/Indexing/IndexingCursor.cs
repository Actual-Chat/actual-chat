using OpenSearch.Client;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

internal interface IIndexingCursor<TState> where TState : class
{
    Task<TState?> Load(Id key, CancellationToken cancellationToken);
    Task Save(Id key, TState state, CancellationToken cancellationToken);
}

/// <summary>
/// This class is intended to store a state of a stream
/// directly in the OpenSearch metadata index.
/// </summary>
/// <typeparam name="TState">State to store</typeparam>
internal class IndexingCursor<TState>(
    IOpenSearchClient client,
    IIndexSettingsSource indexSettingsSource
) : IIndexingCursor<TState> where TState: class
{
    private IndexSettings? _indexSettings;
    private IndexSettings IndexSettings => _indexSettings ??= indexSettingsSource.GetSettings<TState>();

    public async Task<TState?> Load(Id key, CancellationToken cancellationToken)
    {
        var request = new GetRequest(IndexSettings.IndexName, key);
        var result = await client.GetAsync<TState>(
                request,
                cancellationToken
            )
            .ConfigureAwait(false);
        result.AssertSuccess(allowNotFound: true);
        return result.Found ? result.Source : null;
    }

    public async Task Save(Id key, TState state, CancellationToken cancellationToken)
    {
        var response = await client.IndexAsync(
                state,
                e => e
                    .Index(IndexSettings.IndexName)
                    .Id(key),
                cancellationToken
            )
            .ConfigureAwait(true);

        // TODO: figure out why the code below doesn't work. Probably serialization related issue.
        // var request = new UpdateRequest<TState, TState>(IndexSettings.IndexName, key) {
        //     Doc = state,
        //     DocAsUpsert = true,
        // };
        // var result = await client.UpdateAsync(
        //         request,
        //         cancellationToken
        //     )
        //     .ConfigureAwait(false);
        response.AssertSuccess();
    }
}
