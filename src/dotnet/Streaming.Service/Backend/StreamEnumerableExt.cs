namespace ActualChat.Streaming;

public static class StreamEnumerableExt
{
    /// <summary>
    /// Returns only those items whose stream's origin node is currently
    /// <see cref="MeshNodeState.Online"/> according to <paramref name="meshState"/>.
    /// </summary>
    public static IEnumerable<T> WhereAlive<T>(
        this IEnumerable<T> items,
        MeshState meshState,
        Func<T, StreamId> streamIdSelector)
    {
        foreach (var item in items) {
            var node = meshState[streamIdSelector(item).NodeRef];
            if (node is { State: MeshNodeState.Online })
                yield return item;
        }
    }
}
