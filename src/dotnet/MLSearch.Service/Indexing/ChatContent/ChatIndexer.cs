
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
    // private readonly Dictionary<string, ChatSlice> _docs = new(StringComparer.Ordinal);
    // private readonly Dictionary<ChatEntryId, List<ChatSlice>> _docsByEntry = [];

    private readonly List<ChatEntry> _buffer = [];
    private readonly Dictionary<string, ChatSlice> _outUpdates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _outRemoves = new(StringComparer.Ordinal);

    public async Task InitAsync(ChatEntryCursor cursor, CancellationToken cancellationToken)
    {
        _cursor = cursor;
        var tailDocuments = await documentLoader.LoadTailAsync(cursor, cancellationToken).ConfigureAwait(false);
        foreach (var document in tailDocuments) {
//            _docs.Add(document.Id, document);
            _tailDocs.Add(document.Id, document);
//            FillDocumentLookup(document);
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
                    // _docs.Remove(docId);
                    _outUpdates.Remove(docId);
                    _outRemoves.Add(docId);
                }
            }
            // Add new documents to output buffer and update caches
            foreach (var newDoc in newDocuments) {
                _outUpdates[newDoc.Id] = newDoc;
                // _docs[newDoc.Id] = newDoc;
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
        // // Reset document cache
        // _docs.Clear();
        // _docs.AddRange(_tailDocs);
        // // Reset document lookup
        // _docsByEntry.Clear();
        // foreach (var doc in _docs.Values) {
        //     FillDocumentLookup(doc);
        // }
        // Clear output buffers
        _outUpdates.Clear();
        _outRemoves.Clear();
        // Update cursor value
        _cursor = _nextCursor = _buffer.Select(e => new ChatEntryCursor(e)).Append(_nextCursor).Max()!;
        return _cursor;
    }

    private void FillDocumentLookup(ChatSlice document)
    {
        // foreach (var (entryId, _) in document.Metadata.ChatEntries) {
        //     if (!_docsByEntry.TryGetValue(entryId, out var entryDocs)) {
        //         entryDocs = [];
        //         _docsByEntry.Add(entryId, entryDocs);
        //     }
        //     entryDocs.Add(document);
        // }
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

    private async Task<SourceEntries> GetSourceEntriesAsync(ChatEntry entry, IReadOnlyCollection<ChatSlice> associatedDocuments, CancellationToken cancellationToken)
    {
        var docs = new List<ChatSlice>(associatedDocuments);
        docs.Sort(CompareSlices);

        var entries = await chatEntryLoader
            .LoadByIdsAsync(docs.SelectMany(doc => doc.Metadata.ChatEntries).Select(e => e.Id), cancellationToken)
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
    {
        var entryDocs = await documentLoader.LoadByEntryIdsAsync([entry.Id], cancellationToken).ConfigureAwait(false);
        return entryDocs;
        // if (!_docsByEntry.TryGetValue(entry.Id, out var entryDocs) || !IsEntryCovered(entry, entryDocs)) {
        //     var newEntryDocs = await documentLoader.LoadByEntryIdsAsync([entry.Id], cancellationToken).ConfigureAwait(false);
        //     foreach (var doc in entryDocs ?? []) {

        //     }
        //     entryDocs = [.. newEntryDocs];
        //     _docsByEntry[entry.Id] = entryDocs;
        // }
        // return entryDocs;
    }

    // private static bool IsEntryCovered(ChatEntry entry, List<ChatSlice> entryDocs)
    // {
    //     var textLen = entry.Content.Length;
    //     var intervals = new List<(int Start, int End)>(entryDocs.Count);
    //     foreach (var doc in entryDocs) {
    //         var docEntries = doc.Metadata.ChatEntries;
    //         for (int i = 0, len = docEntries.Length; i < len; i++) {
    //             if (docEntries[i].Id != entry.Id) {
    //                 continue;
    //             }
    //             var (isFirst, isLast) = (i == 0, i == len-1);
    //             var start = (isFirst ? doc.Metadata.StartOffset : null) ?? 0;
    //             var end = (isLast ? doc.Metadata.EndOffset : null) ?? textLen;
    //             intervals.Add((start, end));
    //         }
    //     }

    //     intervals.Sort();
    //     var count = intervals.Count;
    //     return count > 0
    //         && intervals[0].Start==0
    //         && intervals[count-1].End==textLen
    //         && intervals.Zip(intervals.Skip(1)).All(args => { var (a, b) = args; return a.End == b.Start; });
    // }
}
