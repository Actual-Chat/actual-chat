using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.Search;
using OpenSearch.Client;
using IndexedEntry = ActualChat.MLSearch.Documents.IndexedEntry;

namespace ActualChat.MLSearch;

public static class ChatEntryExt
{
    public static IndexedEntry ToIndexedEntry(this ChatEntry entry)
        => new() {
            Id = entry.Id.AsTextEntryId(),
            Content = entry.Content,
            At = entry.GetIndexedEntryDate(),
        };

    public static IEnumerable<IndexedEntry> ToIndexedEntries(this IEnumerable<ChatEntry> entries)
        => entries.Select(x => x.ToIndexedEntry());

    public static Moment GetIndexedEntryDate(this ChatEntry entry)
        => entry.EndsAt ?? entry.ContentEndsAt ?? entry.BeginsAt;
}
