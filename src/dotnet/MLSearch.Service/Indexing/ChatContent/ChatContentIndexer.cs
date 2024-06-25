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
        Create,
        Update,
        Delete,
    }

    private class EntryBuffer : IReadOnlyCollection<ChatEntry>
    {
        private readonly LinkedList<ChatEntry> _entries = [];
        private readonly Dictionary<ChatEntryId, LinkedListNode<ChatEntry>> _nodeMap = [];

        public int Count => _entries.Count;
        public IEnumerator<ChatEntry> GetEnumerator() => _entries.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Contains(ChatEntry entry) => _nodeMap.ContainsKey(entry.Id);

        public void AddOrUpdate(ChatEntry entry)
        {
            if (_nodeMap.TryGetValue(entry.Id, out var node)) {
                node.Value = entry;
                return;
            }

            node = new LinkedListNode<ChatEntry>(entry);
            _nodeMap.Add(entry.Id, node);
            _entries.AddLast(node);
        }

        public bool Remove(ChatEntry entry)
        {
            if (_nodeMap.Remove(entry.Id, out var node)) {
                _entries.Remove(node);
                return true;
            }
            return false;
        }
    }

    private ChatContentCursor _cursor = new(0, 0);
    private ChatContentCursor _nextCursor = new(0, 0);

    private readonly Dictionary<string, ChatSlice> _tailDocs = new(StringComparer.Ordinal);

    private readonly EntryBuffer _buffer = [];
    private readonly Dictionary<string, ChatSlice> _outUpdates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _outRemoves = new(StringComparer.Ordinal);

    public ChatContentCursor Cursor => _cursor;
    public IReadOnlyDictionary<string, ChatSlice> TailDocs => _tailDocs;
    public IReadOnlyCollection<ChatEntry> Buffer => _buffer;
    public IReadOnlyDictionary<string, ChatSlice> OutUpdates => _outUpdates;
    public IReadOnlySet<string> OutRemoves => _outRemoves;

    public int MaxTailSetSize { get; init; } = 5;

    public async Task InitAsync(ChatContentCursor cursor, CancellationToken cancellationToken)
    {
        _cursor = cursor;
        var tailDocuments = await documentLoader.LoadTailAsync(cursor, MaxTailSetSize, cancellationToken).ConfigureAwait(false);
        foreach (var document in tailDocuments) {
            _tailDocs.Add(document.Id, document);
        }
    }

    public async ValueTask ApplyAsync(ChatEntry entry, CancellationToken cancellationToken)
    {
        if (_buffer.Contains(entry)) {
            // We can assume there is no entry presence in index or other output buffers
            if (entry.IsRemoved) {
                _buffer.Remove(entry);
                return;
            }
            _buffer.AddOrUpdate(entry);
            return;
        }
        // Lookup for the entry related documents
        var existingDocuments = await LookupDocumentsAsync(entry, cancellationToken).ConfigureAwait(false);
        if (entry.IsRemoved && existingDocuments.Count == 0) {
            // There is no existing docs, so we don't have to change anything.
            return;
        }

        var eventType = entry.IsRemoved ? ChatEventType.Delete
            : existingDocuments.Count==0 ? ChatEventType.Create : ChatEventType.Update;

        if (eventType==ChatEventType.Create) {
            // Buffer new entries until Flush
            _buffer.AddOrUpdate(entry);
        }
        else {
            // Now for updates and removes load all corresponding entries (we have ids in docs)
            var sourceEntries = await GetSourceEntriesAsync(entry, existingDocuments, cancellationToken).ConfigureAwait(false);

            // Send source entries to build document(s)
            var newDoc = sourceEntries.Entries.Count > 0
                ? await BuildDocumentAsync(sourceEntries, cancellationToken).ConfigureAwait(false)
                : null;

            // Now we have existing documents and new documents
            // So delete all existing documents where there is no corresponding new one
            foreach (var docId in existingDocuments.Select(doc => doc.Id)) {
                var isDeleted = newDoc?.Id.Equals(docId, StringComparison.Ordinal) != true;
                if (isDeleted) {
                    _tailDocs.Remove(docId);
                    _outUpdates.Remove(docId);
                    _outRemoves.Add(docId);
                }
            }
            // Add new document to output buffer and update caches
            if (newDoc != null) {
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
            var newDoc = await BuildDocumentAsync(entrySet, cancellationToken).ConfigureAwait(false);
            // Append each new doc to the output
            _outUpdates[newDoc.Id] = newDoc;
            // Save new docs as potential tails for a future processing
            _tailDocs[newDoc.Id] = newDoc;
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
        IReadOnlyCollection<ChatEntry> bufferedEntries,
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
                yield return new SourceEntries(null, null, [.. tailEntries]);
                tailEntries.Clear();
            }
        }

        if (tailEntries.Count > 0) {
            yield return new SourceEntries(null, null, tailEntries);
        }
    }

    private async Task<IReadOnlyList<ChatEntry>> LoadByIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken) => await chatsBackend.GetEntries(entryIds, true, cancellationToken).ConfigureAwait(false);

    private async Task<ChatSlice> BuildDocumentAsync(SourceEntries sourceEntries, CancellationToken cancellationToken)
        => await documentMapper.MapAsync(sourceEntries, cancellationToken).ConfigureAwait(false);

    private async Task<SourceEntries> GetSourceEntriesAsync(ChatEntry entry, IReadOnlyCollection<ChatSlice> associatedDocuments, CancellationToken cancellationToken)
    {
        var docs = new List<ChatSlice>(associatedDocuments);
        docs.Sort(CompareSlices);

        var order = 0;
        var entryOrder = new Dictionary<ChatEntryId, int>();
        var entryIds = new List<ChatEntryId>();
        foreach (var entryId in docs.SelectMany(doc => doc.Metadata.ChatEntries).Select(e => e.Id)) {
            if (entryOrder.TryAdd(entryId, order)) {
                if (entryId != entry.Id) {
                    entryIds.Add(entryId);
                }
                order++;
            }
        }
        var entries = (await LoadByIdsAsync(entryIds, cancellationToken).ConfigureAwait(false))
            .Append(entry)
            .Where(e => !e.IsRemoved)
            .ToList();
        entries.Sort((a, b) => entryOrder[a.Id].CompareTo(entryOrder[b.Id]));

        var firstMeta = docs[0].Metadata;
        var lastMeta = docs[docs.Count - 1].Metadata;
        var startOffset = firstMeta.ChatEntries[0].Id == entry.Id ? default : firstMeta.StartOffset;
        var endOffset = lastMeta.ChatEntries[lastMeta.ChatEntries.Length - 1].Id == entry.Id ? default : lastMeta.EndOffset;

        return new SourceEntries(startOffset, endOffset, entries);

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
