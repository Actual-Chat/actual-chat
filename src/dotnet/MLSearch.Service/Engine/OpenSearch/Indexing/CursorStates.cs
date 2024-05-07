using OpenSearch.Client;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Indexing;
using Microsoft.Extensions.Options;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

/// <summary>
/// This class is intended to store a state of a stream
/// directly in the OpenSearch metadata index.
/// </summary>
/// <typeparam name="TState">State to store</typeparam>
internal sealed class CursorStates<TState> : ICursorStates<TState>, IDisposable
    where TState: class
{
    private readonly string _cursorIndexName;
    private readonly IOpenSearchClient _openSearch;
    private readonly IOptionsMonitor<IndexSettings> _indexSettingsMonitor;
    private readonly IDisposable? _indexSettingsChangeSubscription;
    private IndexSettings? _indexSettings;

    public CursorStates(
        string cursorIndexName,
        IOpenSearchClient openSearch,
        IOptionsMonitor<IndexSettings> indexSettingsMonitor
    )
    {
        _cursorIndexName = cursorIndexName;
        _openSearch = openSearch;
        _indexSettingsMonitor = indexSettingsMonitor;
        _indexSettingsChangeSubscription = _indexSettingsMonitor.OnChange((_, indexName) => {
            if (string.Equals(indexName, _cursorIndexName, StringComparison.Ordinal)) {
                _indexSettings = null;
            }
        });
    }

    private IndexSettings IndexSettings => _indexSettings ??= _indexSettingsMonitor.Get(_cursorIndexName);

    void IDisposable.Dispose() => _indexSettingsChangeSubscription?.Dispose();

    public async Task<TState?> LoadAsync(string key, CancellationToken cancellationToken)
    {
        var request = new GetRequest(IndexSettings.IndexName, key);
        var result = await _openSearch.GetAsync<TState>(
                request,
                cancellationToken
            )
            .ConfigureAwait(false);
        result.AssertSuccess(allowNotFound: true);
        return result.Found ? result.Source : null;
    }

    public async Task SaveAsync(string key, TState state, CancellationToken cancellationToken)
    {
        var response = await _openSearch.IndexAsync(
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
