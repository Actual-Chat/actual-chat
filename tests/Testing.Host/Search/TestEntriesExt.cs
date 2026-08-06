
namespace ActualChat.Testing.Host;

public static class TestEntriesExt
{
    public static List<ChatEntry> Accessible(
        this IReadOnlyDictionary<TestEntryKey, ChatEntry> entries,
        string? filter = null)
        => entries.Where(x => x.Key.ChatKey.MustJoin)
            .Where(x => filter.IsNullOrEmpty() || x.Value.Content.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToList();

    public static List<ChatEntry> AccessibleFromJoinedPrivatePlace2(
        this IReadOnlyDictionary<TestEntryKey, ChatEntry> entries,
        string? filter = null)
        => entries.Where(x => x.Key.ChatKey.PlaceKey == TestPlaceKey.JoinedPrivatePlace2 && x.Key.ChatKey.MustJoin)
            .Where(x => filter.IsNullOrEmpty() || x.Value.Content.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToList();

    public static List<ChatEntry> Accessible1(
        this IReadOnlyDictionary<TestEntryKey, ChatEntry> entries,
        string? filter = null)
        => entries.Where(x => x.Key.ChatKey.MustJoin && x.Key.Index == 0)
            .Where(x => filter.IsNullOrEmpty() || x.Value.Content.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToList();

    public static List<ChatEntry> Other(
        this IReadOnlyDictionary<TestEntryKey, ChatEntry> entries,
        string? filter = null)
        => entries.Where(x => !x.Key.ChatKey.MustJoin)
            .Where(x => filter.IsNullOrEmpty() || x.Value.Content.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToList();
}
