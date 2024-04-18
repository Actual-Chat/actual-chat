using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal record ChatCursor(long LastEntryVersion, long LastEntryLocalId) : IComparable<ChatCursor>
{
    public ChatCursor(ChatEntry chatEntry) : this(chatEntry.Version, chatEntry.LocalId)
    { }

    public int CompareTo(ChatCursor? other)
        => other is null ? 1 : (LastEntryVersion, LastEntryLocalId).CompareTo((other.LastEntryVersion, other.LastEntryLocalId));

    public static bool operator <(ChatCursor a, ChatCursor b)
        => a is null ? b is not null : a.CompareTo(b) < 0;
    public static bool operator <=(ChatCursor a, ChatCursor b)
        => a is null || a.CompareTo(b) <= 0;
    public static bool operator >(ChatCursor a, ChatCursor b)
        => a?.CompareTo(b) > 0;
    public static bool operator >=(ChatCursor a, ChatCursor b)
        => a is null ? b is null : a.CompareTo(b) >= 0;
}
