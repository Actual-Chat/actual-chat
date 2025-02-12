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

        IReadOnlyList<ChatEntry> tailEntries = Array.Empty<ChatEntry>();
        // Preload document tails
        if (lastTailDocument is not null)
            tailEntries = await documentEntriesLoader.LoadTailEntries(lastTailDocument, cancellationToken)
                .ConfigureAwait(false);

        var extractorState = new ExtractorState(new EntryGroupBuilder(tailEntries), new EntryGroupBuilder());
        var extractResult = await entryGroupExtractor.ExtractGroups(extractorState, bufferedEntries, cancellationToken)
            .ConfigureAwait(false);

        foreach (var group in extractResult.Groups)
            yield return new SourceEntries(null, null, group.Entries);

        // No other way to preserve tail for processing - so let's return it to be used in the next batch as tail document
        if (extractResult.State.CurrentGroup is null)
            yield break;

        List<ChatEntry> tail = [..extractResult.State.CurrentGroup.Entries];
        if (extractResult.State.CurrentChunk is not null)
            tail.AddRange(extractResult.State.CurrentChunk.Entries);
        if (tail.Count > 0)
            yield return new SourceEntries(null, null, tail);

        // TODO(AK): There is no processing of replies
    }
}
