namespace ActualChat.MLSearch.Indexing.ChatContent;
internal record ChatEntryCursor(long LastEntryLocalId, long LastEntryVersion);

internal interface IChatEntryCursorStates
{
    Task<ChatEntryCursor> LoadAsync(ChatId key, CancellationToken cancellationToken);
    Task SaveAsync(ChatId key, ChatEntryCursor state, CancellationToken cancellationToken);
}

internal class ChatEntryCursorStates(ICursorStates<ChatEntryCursor> cursorStates): IChatEntryCursorStates
{
    public async Task<ChatEntryCursor> LoadAsync(ChatId key, CancellationToken cancellationToken)
        => (await cursorStates.Load(key, cancellationToken).ConfigureAwait(false)) ?? new(0, 0);

    public async Task SaveAsync(ChatId key, ChatEntryCursor state, CancellationToken cancellationToken)
        => await cursorStates.Save(key, state, cancellationToken).ConfigureAwait(false);
}
