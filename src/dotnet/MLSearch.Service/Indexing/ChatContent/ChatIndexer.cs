
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatIndexer
{
    Task InitAsync(ChatEntryCursor cursor, CancellationToken cancellationToken);
    ValueTask ApplyAsync(ChatEntry entry, CancellationToken cancellationToken);
    Task<ChatEntryCursor> FlushAsync(CancellationToken cancellationToken);
}

internal sealed class ChatIndexer(
    IChatEntryLoader chatEntryLoader,
    IDocumentLoader documentLoader,
    IDocumentMapper<ChatEntry, ChatEntry, ChatSlice> documentMapper,
    ISink<ChatSlice, string> sink
) : IChatIndexer
{
    private const int MaxTailSetSize = 5;
    private record SourceEntries(int StartOffset, int? EndOffset, IReadOnlyList<ChatEntry> Entries);

    private ChatEntryCursor _cursor = new(0, 0);
    private ChatEntryCursor _nextCursor = new(0, 0);

    private readonly Dictionary<string, ChatSlice> _tailDocs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChatSlice> _docs = new(StringComparer.Ordinal);
    private readonly Dictionary<ChatEntryId, List<ChatSlice>> _docsByEntry = [];

    private readonly List<ChatEntry> _buffer = [];
    private readonly Dictionary<string, ChatSlice> _outUpdates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _outRemoves = new(StringComparer.Ordinal);

    public async Task InitAsync(ChatEntryCursor cursor, CancellationToken cancellationToken)
    {
        _cursor = cursor;
        var tailDocuments = await documentLoader.LoadTailAsync(cancellationToken).ConfigureAwait(false);
        foreach (var document in tailDocuments) {
            _docs.Add(document.Id, document);
            _tailDocs.Add(document.Id, document);
            FillDocumentLookup(document);
        }
    }

    public async ValueTask ApplyAsync(ChatEntry entry, CancellationToken cancellationToken)
    {
        var eventType = entry.IsRemoved ? ChatEventType.Delete
            : entry.LocalId > _cursor.LastEntryLocalId ? ChatEventType.Create : ChatEventType.Update;

        // Lookup for the entry related documents
        var existingDocuments = await LookupDocumentsAsync(entry.Id, cancellationToken).ConfigureAwait(false);
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
            var sourceEntries = await GetSourceEntriesAsync(entry, eventType, existingDocuments, cancellationToken).ConfigureAwait(false);

            // Send source entries to build document(s)
            var newDocuments = await BuildDocumentsAsync(sourceEntries, cancellationToken).ConfigureAwait(false);

            // Now we have existing documents and new documents
            // So delete all existing documents where there is no corresponding new one
            foreach (var docId in existingDocuments.Select(doc => doc.Id)) {
                var isDeleted = !newDocuments.Any(newDoc => newDoc.Id.Equals(docId, StringComparison.Ordinal));
                if (isDeleted) {
                    _tailDocs.Remove(docId);
                    _docs.Remove(docId);
                    _outUpdates.Remove(docId);
                    _outRemoves.Add(docId);
                }
            }
            // Add new documents to output buffer and update caches
            foreach (var newDoc in newDocuments) {
                _outUpdates[newDoc.Id] = newDoc;
                _docs[newDoc.Id] = newDoc;
                if (_tailDocs.ContainsKey(newDoc.Id)) {
                    _tailDocs[newDoc.Id] = newDoc;
                }
            }
            // Update next cursor value
            _nextCursor = new ChatEntryCursor(entry);
        }
    }

    public async Task<ChatEntryCursor> FlushAsync(CancellationToken cancellationToken)
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
        // Reset document cache
        _docs.Clear();
        _docs.AddRange(_tailDocs);
        // Reset document lookup
        _docsByEntry.Clear();
        foreach (var doc in _docs.Values) {
            FillDocumentLookup(doc);
        }
        // Clear output buffers
        _outUpdates.Clear();
        _outRemoves.Clear();
        // Update cursor value
        _cursor = _nextCursor = _buffer.Select(e => new ChatEntryCursor(e)).Append(_nextCursor).Max()!;
        return _cursor;
    }

    private void FillDocumentLookup(ChatSlice document)
    {
        foreach (var (entryId, _) in document.Metadata.ChatEntries) {
            if (!_docsByEntry.TryGetValue(entryId, out var entryDocs)) {
                entryDocs = [];
                _docsByEntry.Add(entryId, entryDocs);
            }
            entryDocs.Add(document);
        }
    }

    private async IAsyncEnumerable<SourceEntries> ArrangeBufferedEntriesAsync(
        IReadOnlyList<ChatEntry> bufferedEntries,
        IReadOnlyCollection<ChatSlice> tailDocuments,
        CancellationToken cancellationToken)
    {
        yield break;
    }

    private async Task<IReadOnlyCollection<ChatSlice>> BuildDocumentsAsync(SourceEntries sourceEntries, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private async Task<SourceEntries> GetSourceEntriesAsync(ChatEntry entry, ChatEventType eventType, IReadOnlyCollection<ChatSlice> associatedDocuments, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private async Task<IReadOnlyCollection<ChatSlice>> LookupDocumentsAsync(ChatEntryId id, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
