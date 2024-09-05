using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing.ChatContent;

[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
internal record ChatEntryCursor(long LastEntryVersion, long LastEntryLocalId) : IComparable<ChatEntryCursor>
{
    public ChatEntryCursor(ChatEntry chatEntry) : this(chatEntry.Version, chatEntry.LocalId)
    { }

    public int CompareTo(ChatEntryCursor? other)
        => other is null ? 1 : (LastEntryVersion, LastEntryLocalId).CompareTo((other.LastEntryVersion, other.LastEntryLocalId));

    public static bool operator <(ChatEntryCursor? a, ChatEntryCursor? b)
        => a is null ? b is not null : a.CompareTo(b) < 0;
    public static bool operator <=(ChatEntryCursor? a, ChatEntryCursor? b)
        => a is null || a.CompareTo(b) <= 0;
    public static bool operator >(ChatEntryCursor? a, ChatEntryCursor? b)
        => a?.CompareTo(b) > 0;
    public static bool operator >=(ChatEntryCursor? a, ChatEntryCursor? b)
        => a is null ? b is null : a.CompareTo(b) >= 0;
}
