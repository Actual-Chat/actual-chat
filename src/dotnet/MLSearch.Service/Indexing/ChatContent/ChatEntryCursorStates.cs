using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing.ChatContent;
internal record ChatEntryCursor(long LastEntryVersion, long LastEntryLocalId) : IComparable<ChatEntryCursor>
{
    public ChatEntryCursor(ChatEntry chatEntry) : this(chatEntry.Version, chatEntry.LocalId)
    { }

    public int CompareTo(ChatEntryCursor? other)
        => other is null ? 1 : (LastEntryVersion, LastEntryLocalId).CompareTo((other.LastEntryVersion, other.LastEntryLocalId));

    public static bool operator <(ChatEntryCursor a, ChatEntryCursor b)
        => a is null ? b is not null : a.CompareTo(b) < 0;
    public static bool operator <=(ChatEntryCursor a, ChatEntryCursor b)
        => a is null || a.CompareTo(b) <= 0;
    public static bool operator >(ChatEntryCursor a, ChatEntryCursor b)
        => a?.CompareTo(b) > 0;
    public static bool operator >=(ChatEntryCursor a, ChatEntryCursor b)
        => a is null ? b is null : a.CompareTo(b) >= 0;
}

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
