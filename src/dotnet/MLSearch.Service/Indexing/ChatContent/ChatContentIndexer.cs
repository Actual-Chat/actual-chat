using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatContentIndexer
{
    Task InitAsync(ChatContentCursor cursor, CancellationToken cancellationToken);
    ValueTask ApplyAsync(ChatEntry entry, CancellationToken cancellationToken);
    Task<ChatContentCursor> FlushAsync(CancellationToken cancellationToken);
}

internal sealed class ChatContentIndexer(
    IChatsBackend chatsBackend,
    IChatContentDocumentLoader documentLoader,
    IChatContentMapper documentMapper,
    ISink<ChatSlice, string> sink
) : IChatContentIndexer
{
    private enum ChatEventType
    {
        None,
        Create,
        Update,
        Delete,
    }

    private const int MaxTailSetSize = 5;
    private ChatContentCursor _cursor = new(0, 0);
    private ChatContentCursor _nextCursor = new(0, 0);

    private readonly Dictionary<string, ChatSlice> _tailDocs = new(StringComparer.Ordinal);

    private readonly List<ChatEntry> _buffer = [];
    private readonly Dictionary<string, ChatSlice> _outUpdates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _outRemoves = new(StringComparer.Ordinal);

    public async Task InitAsync(ChatContentCursor cursor, CancellationToken cancellationToken)
    {
        _cursor = cursor;
        var tailDocuments = await documentLoader.LoadTailAsync(cursor, cancellationToken).ConfigureAwait(false);
        foreach (var document in tailDocuments) {
            _tailDocs.Add(document.Id, document);
        }
    }

    public async ValueTask ApplyAsync(ChatEntry entry, CancellationToken cancellationToken)
    {
        var eventType = entry.IsRemoved ? ChatEventType.Delete
            : entry.LocalId > _cursor.LastEntryLocalId ? ChatEventType.Create : ChatEventType.Update;

        // Lookup for the entry related documents
        var existingDocuments = await LookupDocumentsAsync(entry, cancellationToken).ConfigureAwait(false);
        // Adjust event type
        eventType = eventType switch {
            ChatEventType.Create when existingDocuments.Count!=0 => ChatEventType.Update,
            ChatEventType.Update when existingDocuments.Count==0 => ChatEventType.Create,
            ChatEventType.Delete when existingDocuments.Count==0 => ChatEventType.None,
            _ => eventType,
        };

        if (eventType==ChatEventType.Create) {
            // Buffer new entries until Flush
            _buffer.Add(entry);
        }
        else {
            // Now for updates and removes load all corresponding entries (we have ids in docs)
            var sourceEntries = await GetSourceEntriesAsync(entry, existingDocuments, cancellationToken).ConfigureAwait(false);

            // Send source entries to build document(s)
            var newDocuments = await BuildDocumentsAsync(sourceEntries, cancellationToken).ConfigureAwait(false);

            // Now we have existing documents and new documents
            // So delete all existing documents where there is no corresponding new one
            foreach (var docId in existingDocuments.Select(doc => doc.Id)) {
                var isDeleted = !newDocuments.Any(newDoc => newDoc.Id.Equals(docId, StringComparison.Ordinal));
                if (isDeleted) {
                    _tailDocs.Remove(docId);
                    _outUpdates.Remove(docId);
                    _outRemoves.Add(docId);
                }
            }
            // Add new documents to output buffer and update caches
            foreach (var newDoc in newDocuments) {
                _outUpdates[newDoc.Id] = newDoc;
                if (_tailDocs.ContainsKey(newDoc.Id)) {
                    _tailDocs[newDoc.Id] = newDoc;
                }
            }
            // Update next cursor value
            _nextCursor = new ChatContentCursor(entry);
        }
    }

    public async Task<ChatContentCursor> FlushAsync(CancellationToken cancellationToken)
    {
        // Process buffer & tail
        await foreach (var entrySet in ArrangeBufferedEntriesAsync(_buffer, _tailDocs.Values, cancellationToken).ConfigureAwait(false)) {
            var newDocuments = await BuildDocumentsAsync(entrySet, cancellationToken).ConfigureAwait(false);
            foreach (var newDoc in newDocuments) {
                // Append each new doc to the output
                _outUpdates[newDoc.Id] = newDoc;
                // Save new docs as potential tails for a future processing
                _tailDocs[newDoc.Id] = newDoc;
            }
        }
        // Apply changes to the document index
        await sink.ExecuteAsync(_outUpdates.Values, _outRemoves, cancellationToken).ConfigureAwait(false);
        // Trim tail document set
        var tailSet = new PriorityQueue<ChatSlice, long>(MaxTailSetSize + 1);
        foreach (var tailDoc in _tailDocs.Values) {
            if (tailSet.Count < MaxTailSetSize) {
                tailSet.Enqueue(tailDoc, tailDoc.Version);
            }
            else {
                _ = tailSet.EnqueueDequeue(tailDoc, tailDoc.Version);
            }
        }
        _tailDocs.Clear();
        foreach (var (tailDoc, _) in tailSet.UnorderedItems) {
            _tailDocs.Add(tailDoc.Id, tailDoc);
        }
        // Clear output buffers
        _outUpdates.Clear();
        _outRemoves.Clear();
        // Update cursor value
        _cursor = _nextCursor = _buffer.Select(e => new ChatContentCursor(e)).Append(_nextCursor).Max()!;
        return _cursor;
    }

    private async IAsyncEnumerable<SourceEntries> ArrangeBufferedEntriesAsync(
        IReadOnlyList<ChatEntry> bufferedEntries,
        IReadOnlyCollection<ChatSlice> tailDocuments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // TODO: for now we just group buffered messages by three into a document
        // but in future we want to select document depending on the content
        var tailEntryIds = tailDocuments
            .Where(doc => doc.Metadata.ChatEntries.Length < 3)
            .Take(1)
            .SelectMany(doc => doc.Metadata.ChatEntries)
            .Select(e => e.Id);
        var tailEntries = new List<ChatEntry>(
            await LoadByIdsAsync(tailEntryIds, cancellationToken).ConfigureAwait(false));
        foreach (var entry in bufferedEntries) {
            tailEntries.Add(entry);
            if (tailEntries.Count == 3) {
                yield return new SourceEntries(0, null, [.. tailEntries]);
                tailEntries.Clear();
            }
        }

        yield return new SourceEntries(0, null, tailEntries);
    }

    private async Task<IReadOnlyList<ChatEntry>> LoadByIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken) => await chatsBackend.GetEntries(entryIds, true, cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyCollection<ChatSlice>> BuildDocumentsAsync(SourceEntries sourceEntries, CancellationToken cancellationToken)
        => await documentMapper.MapAsync(sourceEntries, cancellationToken).ConfigureAwait(false);

    private async Task<SourceEntries> GetSourceEntriesAsync(ChatEntry entry, IReadOnlyCollection<ChatSlice> associatedDocuments, CancellationToken cancellationToken)
    {
        var docs = new List<ChatSlice>(associatedDocuments);
        docs.Sort(CompareSlices);

        var entries = await LoadByIdsAsync(docs.SelectMany(doc => doc.Metadata.ChatEntries).Select(e => e.Id), cancellationToken)
            .ConfigureAwait(false);

        return new SourceEntries(docs[0].Metadata.StartOffset ?? 0, docs[docs.Count-1].Metadata.EndOffset, entries);

        int CompareSlices(ChatSlice a, ChatSlice b)
            => GetStartOffset(a, entry).CompareTo(GetStartOffset(b, entry));

        static int GetStartOffset(ChatSlice slice, in ChatEntry entry)
        {
            var isFirst = slice.Metadata.ChatEntries[0].Id == entry.Id;
            return isFirst ? slice.Metadata.StartOffset ?? 0 : 0;
        }
    }

    private async Task<IReadOnlyCollection<ChatSlice>> LookupDocumentsAsync(ChatEntry entry, CancellationToken cancellationToken)
        => await documentLoader.LoadByEntryIdsAsync([entry.Id], cancellationToken).ConfigureAwait(false);
}
