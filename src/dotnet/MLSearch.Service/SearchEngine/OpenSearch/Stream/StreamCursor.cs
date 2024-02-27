using OpenSearch.Client;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch.Stream;

/// <summary>
/// This class is intended to store a state of a stream
/// directly in the OpenSearch metadata index.
/// </summary>
/// <typeparam name="TState">State to store</typeparam>
public class StreamCursor<TState>(
    IOpenSearchClient client,
    IndexName metadataIndexName
)
{
    public async Task<TState?> Load()
    {
        return default;
    }

    public async Task Save(TState state)
    { }
}
