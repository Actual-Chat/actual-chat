namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatCursorStates
{
    Task<ChatCursor> LoadAsync(ChatId key, CancellationToken cancellationToken);
    Task SaveAsync(ChatId key, ChatCursor state, CancellationToken cancellationToken);
}

internal class ChatCursorStates(ICursorStates<ChatCursor> cursorStates): IChatCursorStates
{
    public async Task<ChatCursor> LoadAsync(ChatId key, CancellationToken cancellationToken)
        => (await cursorStates.Load(key, cancellationToken).ConfigureAwait(false)) ?? new(0, 0);

    public async Task SaveAsync(ChatId key, ChatCursor state, CancellationToken cancellationToken)
        => await cursorStates.Save(key, state, cancellationToken).ConfigureAwait(false);
}
