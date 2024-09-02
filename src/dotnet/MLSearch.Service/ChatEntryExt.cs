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
        };

    public static IEnumerable<IndexedEntry> ToIndexedEntries(this IEnumerable<ChatEntry> entries)
        => entries.Select(x => x.ToIndexedEntry());
}
