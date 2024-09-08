using ActualChat.Chat;

namespace ActualChat.Testing.Host;

public static class TestEntriesExt
{
    public static IEnumerable<ChatEntry> Accessible(
        this IReadOnlyDictionary<TestEntryKey, ChatEntry> entries)
        => entries.Where(x => x.Key.ChatKey.MustJoin).Select(x => x.Value);

    public static IEnumerable<ChatEntry> Accessible1(
        this IReadOnlyDictionary<TestEntryKey, ChatEntry> entries)
        => entries.Where(x => x.Key.ChatKey.MustJoin && x.Key.Index == 0).Select(x => x.Value);

    public static IEnumerable<ChatEntry> Other(
        this IReadOnlyDictionary<TestEntryKey, ChatEntry> entries)
        => entries.Where(x => !x.Key.ChatKey.MustJoin).Select(x => x.Value);
}
