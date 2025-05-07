using ActualChat.Chat;
using IndexedEntry = ActualChat.MLSearch.Documents.IndexedEntry;

namespace ActualChat.MLSearch;

public static class ChatEntryExt
{
    public static IndexedEntry ToIndexedEntry(this ChatEntry entry)
        => new() {
            Id = (TextEntryId)entry.Id,
            Content = entry.Content,
            At = entry.GetIndexedEntryDate(),
        };

    public static IEnumerable<IndexedEntry> ToIndexedEntries(this IEnumerable<ChatEntry> entries)
        => entries.Select(x => x.ToIndexedEntry());

    public static Moment GetIndexedEntryDate(this ChatEntry entry)
        => entry.EndsAt ?? entry.ContentEndsAt ?? entry.BeginsAt;
}
