using ActualChat.Chat;
using ActualChat.Chat.ML;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal class ChatContentArranger4(
    [FromKeyedServices(EntryGroupLimit.Medium)]IEntryGroupExtractor entryGroupExtractor,
    IDocumentEntriesLoader documentEntriesLoader
): IChatContentArranger
{
    public async IAsyncEnumerable<SourceEntries> Arrange(
        IReadOnlyCollection<ChatEntry> bufferedEntries,
        IReadOnlyCollection<ChatSlice> tailDocuments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (bufferedEntries.Count == 0)
            yield break;

        var lastTailDocument = tailDocuments
            .Select(c => new { Document = c, MaxEntryLid = c.Metadata.ChatEntries.Max(x => x.LocalId) })
            .OrderByDescending(c => c.MaxEntryLid)
            .Select(c => c.Document)
            .FirstOrDefault();

        var entryMap = new Dictionary<long, ChatEntry>();

        IReadOnlyList<ChatEntry> tailEntries = Array.Empty<ChatEntry>();
        // Preload document tails
        if (lastTailDocument is not null)
            tailEntries = await documentEntriesLoader.LoadTailEntries(lastTailDocument, cancellationToken)
                .ConfigureAwait(false);
        foreach (var chatEntry in tailEntries)
            entryMap[chatEntry.LocalId] = chatEntry;
        foreach (var chatEntry in bufferedEntries)
            entryMap[chatEntry.LocalId] = chatEntry;

        var currentTextEntries = tailEntries.Select(e => new TextEntry(e)).ToList();
        var extractorState = new ExtractorState(new EntryGroupBuilder(currentTextEntries), new EntryGroupBuilder());
        var bufferedTextEntries = bufferedEntries.Select(e => new TextEntry(e)).ToList();
        var extractResult = entryGroupExtractor.ExtractGroups(extractorState, bufferedTextEntries);

        foreach (var group in extractResult.Groups) {
            var entries = group.Entries.Select(e => entryMap[e.LocalId]).ToList();
            yield return new SourceEntries(null, null, entries);
        }

        // No other way to preserve tail for processing - so let's return it to be used in the next batch as tail document
        if (extractResult.State.CurrentGroup is null)
            yield break;

        List<ChatEntry> tail = extractResult.State.CurrentGroup.Entries.Select(e => entryMap[e.LocalId]).ToList();
        if (extractResult.State.CurrentChunk is not null)
            tail.AddRange(extractResult.State.CurrentChunk.Entries.Select(e => entryMap[e.LocalId]));
        if (tail.Count > 0)
            yield return new SourceEntries(null, null, tail);

        // TODO(AK): There is no processing of replies
    }
}
