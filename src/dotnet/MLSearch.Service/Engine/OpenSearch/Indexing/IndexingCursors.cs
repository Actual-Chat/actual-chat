using OpenSearch.Client;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

/// <summary>
/// This class is intended to store a state of a stream
/// directly in the OpenSearch metadata index.
/// </summary>
/// <typeparam name="TState">State to store</typeparam>
internal class IndexingCursors<TState>(
    IOpenSearchClient client,
    IIndexSettingsSource indexSettingsSource
)
where TState: class
{
    private IndexSettings? _indexSettings;
    private IndexSettings IndexSettings => _indexSettings ??= indexSettingsSource.GetSettings<TState>();

    public async Task<TState?> Load(Id key, CancellationToken cancellationToken)
    {
        var path = new DocumentPath<TState>(key)
            .Index(IndexSettings.CursorIndexName);
        var result = await client.GetAsync<TState>(
                path,
                null,
                cancellationToken
            )
            .ConfigureAwait(false);

        result.AssertSuccess();
        return result.Source;
    }

    public async Task Save(Id key, TState state, CancellationToken cancellationToken)
    {
        var path = new DocumentPath<TState>(key)
            .Index(IndexSettings.CursorIndexName);
        var result = await client.UpdateAsync<TState>(
                path,
                e => e.Upsert(state),
                cancellationToken
            )
            .ConfigureAwait(false);
        result.AssertSuccess();
    }
}
