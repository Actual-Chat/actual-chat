namespace ActualChat.Search;

public static class IndexedChatsBackendExt
{
    public static async IAsyncEnumerable<ApiArray<IndexedChat>> Batches(
        this IIndexedChatsBackend backend,
        Moment minCreatedAt,
        ChatId lastChatId,
        int limit,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var chats = await backend.List(minCreatedAt, lastChatId, limit, cancellationToken)
                .ConfigureAwait(false);
            if (chats.Count == 0)
                yield break;

            yield return chats;

            var lastChat = chats[^1];
            lastChatId = lastChat.Id;
            minCreatedAt = lastChat.ChatCreatedAt;
        }
    }
}
