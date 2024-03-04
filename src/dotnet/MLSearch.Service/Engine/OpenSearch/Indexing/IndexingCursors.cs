using OpenSearch.Client;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using System.Dynamic;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

/// <summary>
/// This class is intended to store a state of a stream
/// directly in the OpenSearch metadata index.
/// </summary>
/// <typeparam name="TState">State to store</typeparam>
internal class IndexingCursors<TState>(
    IOpenSearchClient client,
    IndexName indexName
)
where TState: class
{

    public async Task<TState?> Load(Id key, CancellationToken cancellationToken)
    {
        var request = new GetRequest(indexName, key);
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
        var request = new UpdateRequest<TState, TState>(indexName, key);
        request.Upsert = state;
        var result = await client.UpdateAsync(
                request,
                cancellationToken
            )
            .ConfigureAwait(false);
        result.AssertSuccess();
    }
}
