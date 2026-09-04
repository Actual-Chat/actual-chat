using IndexedEntry = ActualChat.MLSearch.Documents.IndexedEntry;

namespace ActualChat.MLSearch;

public static class ChatEntryExt
{
    public static IndexedEntry ToIndexedEntry(this ChatEntry entry, IMarkupParser markupParser, UserId? authorUserId)
    {
        var markup = markupParser.Parse(entry.Content);
        return new IndexedEntry {
            Id = entry.Id,
            Content = entry.Content,
            Hashtags = HashtagExtractor.Instance.GetTags(markup).ToArray(),
            At = entry.GetIndexedEntryDate(),
            AuthorUserId = authorUserId,
        };
    }

    public static Moment GetIndexedEntryDate(this ChatEntry entry)
        => entry.EndsAt ?? entry.Audio?.ContentEndsAt ?? entry.BeginsAt;
}
