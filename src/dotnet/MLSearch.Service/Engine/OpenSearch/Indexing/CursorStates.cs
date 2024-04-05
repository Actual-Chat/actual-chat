using OpenSearch.Client;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Indexing;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

/// <summary>
/// This class is intended to store a state of a stream
/// directly in the OpenSearch metadata index.
/// </summary>
/// <typeparam name="TState">State to store</typeparam>
internal sealed class CursorStates<TState>(
    string cursorIndexName,
    IOpenSearchClient client,
    IIndexSettingsSource indexSettingsSource
) : ICursorStates<TState> where TState: class
{
    private IndexSettings? _indexSettings;
    private IndexSettings IndexSettings => _indexSettings ??= indexSettingsSource.GetSettings(cursorIndexName);

    public async Task<TState?> Load(string key, CancellationToken cancellationToken)
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

    public async Task Save(string key, TState state, CancellationToken cancellationToken)
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
