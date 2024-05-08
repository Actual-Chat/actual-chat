using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing.ChatContent;

[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
internal record ChatContentCursor(long LastEntryVersion, long LastEntryLocalId) : IComparable<ChatContentCursor>
{
    public ChatContentCursor(ChatEntry chatEntry) : this(chatEntry.Version, chatEntry.LocalId)
    { }

    public int CompareTo(ChatContentCursor? other)
        => other is null ? 1 : (LastEntryVersion, LastEntryLocalId).CompareTo((other.LastEntryVersion, other.LastEntryLocalId));

    public static bool operator <(ChatContentCursor a, ChatContentCursor b)
        => a is null ? b is not null : a.CompareTo(b) < 0;
    public static bool operator <=(ChatContentCursor a, ChatContentCursor b)
        => a is null || a.CompareTo(b) <= 0;
    public static bool operator >(ChatContentCursor a, ChatContentCursor b)
        => a?.CompareTo(b) > 0;
    public static bool operator >=(ChatContentCursor a, ChatContentCursor b)
        => a is null ? b is null : a.CompareTo(b) >= 0;
}
