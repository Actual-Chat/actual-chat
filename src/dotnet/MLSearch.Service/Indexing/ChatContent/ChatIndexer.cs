
using System.Text;
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
    IMarkupParser markupParser,
    IReactionsBackend reactionsBackend,
    ISink<ChatSlice> sink
) : IChatIndexer
{
    private const int MaxTailSetSize = 5;
    private record SourceEntries(int StartOffset, int? EndOffset, IReadOnlyList<ChatEntry> Entries);

    private ChatEntryCursor _cursor = new(0, 0);
    private ChatEntryCursor _nextCursor = new(0, 0);

    private readonly Dictionary<string, ChatSlice> _tailDocs = new(StringComparer.Ordinal);

    private readonly List<ChatEntry> _buffer = [];
    private readonly Dictionary<string, ChatSlice> _outUpdates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _outRemoves = new(StringComparer.Ordinal);

    public async Task InitAsync(ChatEntryCursor cursor, CancellationToken cancellationToken)
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
        // Clear output buffers
        _outUpdates.Clear();
        _outRemoves.Clear();
        // Update cursor value
        _cursor = _nextCursor = _buffer.Select(e => new ChatEntryCursor(e)).Append(_nextCursor).Max()!;
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
        var tailEntries = new List<ChatEntry>(await chatEntryLoader
            .LoadByIdsAsync(tailEntryIds, cancellationToken)
            .ConfigureAwait(false));
        foreach (var entry in bufferedEntries) {
            tailEntries.Add(entry);
            if (tailEntries.Count == 3) {
                yield return new SourceEntries(0, null, [.. tailEntries]);
                tailEntries.Clear();
            }
        }

        yield return new SourceEntries(0, null, tailEntries);
    }

    private async Task<IReadOnlyCollection<ChatSlice>> BuildDocumentsAsync(SourceEntries sourceEntries, CancellationToken cancellationToken)
    {
        // TODO: in the future we may want to split sourse sequence into several documents
        // but for now lets create just single ChatSlice

        var entryCount = sourceEntries.Entries.Count;
        var principalSet = new HashSet<PrincipalId>(entryCount);

        // -- Authors
        principalSet.AddRange(sourceEntries.Entries.Select(e => new PrincipalId(e.AuthorId.Id)));
        var authors = ImmutableArray.CreateBuilder<PrincipalId>(principalSet.Count);
        authors.AddRange(principalSet);

        // -- Chat Entries
        var chatEntries = ImmutableArray.CreateBuilder<ChatSliceEntry>(entryCount);
        chatEntries.AddRange(sourceEntries.Entries.Select(e => new ChatSliceEntry(e.Id, e.LocalId, e.Version)));

        // -- Replies
        const int replyToEstimatedCount = 1;
        var uniqueReplyToEnries = new HashSet<ChatEntryId>(replyToEstimatedCount);
        uniqueReplyToEnries.AddRange(sourceEntries.Entries
            .Where(e => e.RepliedEntryLocalId is not null)
            .Select(e => new ChatEntryId(e.ChatId, ChatEntryKind.Text, e.RepliedEntryLocalId!.Value, AssumeValid.Option)));
        // TODO: We may want to build some summary for the entries we are replying to
        // We may use that summary while building document content later
        var replyToEntries = ImmutableArray.CreateBuilder<ChatEntryId>(uniqueReplyToEnries.Count);
        replyToEntries.AddRange(uniqueReplyToEnries);

        // -- Mentions
        var mentionExtractor = new MentionExtractor();
        principalSet.Clear();
        principalSet.AddRange(sourceEntries.Entries
            .Where(e => e.HasMarkup)
            .SelectMany(e => mentionExtractor.GetMentionIds(markupParser.Parse(e.Content)))
            .Select(mentionId => mentionId.PrincipalId));
        var mentions = ImmutableArray.CreateBuilder<PrincipalId>(principalSet.Count);
        mentions.AddRange(principalSet);

        // -- Reactions
        principalSet.Clear();
        foreach (var entryId in sourceEntries.Entries.Where(e => e.HasReactions).Select(e => e.Id.ToTextEntryId())) {
            var reactionSummary = await reactionsBackend.List(entryId, cancellationToken).ConfigureAwait(false);
            principalSet.AddRange(reactionSummary.SelectMany(s => s.FirstAuthorIds).Select(author => new PrincipalId(author.Id)));
        }
        var reactions = ImmutableArray.CreateBuilder<PrincipalId>(principalSet.Count);
        reactions.AddRange(principalSet);

        // -- Attachments
        var uniqueAttachments = new HashSet<MediaId>(entryCount);
        uniqueAttachments.AddRange(sourceEntries.Entries
            .SelectMany(e => e.Attachments)
            .Select(a => a.MediaId));
        var attachments = ImmutableArray.CreateBuilder<ChatSliceAttachment>(uniqueAttachments.Count);
        attachments.AddRange(uniqueAttachments.Select(mediaId => {
            // TODO: we may want to build summary with help of ML
            const string summary = "No summary yet";
            return new ChatSliceAttachment(mediaId, summary);
        }));

        // -- Timestamp
        var timestamp = sourceEntries.Entries.Select(e => e.BeginsAt).First();

        // -- Content
        var content = sourceEntries.Entries
            .Select((e, i) => {
                var content = e.Content;
                var (isFirst, isLast) = (i == 0, i == entryCount - 1);
                if (isFirst || isLast) {
                    var start = isFirst ? sourceEntries.StartOffset : 0;
                    var end = (isLast ? sourceEntries.EndOffset : null) ?? content.Length;
                    return content.Substring(start, end - start);
                }
                return content;
            })
            .Aggregate(new StringBuilder(), (sb, line) => sb.AppendLine(line));

        var metadata = new ChatSliceMetadata(
            Authors: authors.MoveToImmutable(),
            ChatEntries: chatEntries.MoveToImmutable(),
            StartOffset: sourceEntries.StartOffset,
            EndOffset: sourceEntries.EndOffset,
            ReplyToEntries: replyToEntries.MoveToImmutable(),
            Mentions: mentions.MoveToImmutable(),
            // TODO: talk: seems it's a bit too much.
            Reactions: reactions.MoveToImmutable(),
            Attachments: attachments.MoveToImmutable(),
            // TODO:
            IsPublic: true,
            Language: null,
            // TODO:
            Timestamp: timestamp
        );
        return [new ChatSlice(metadata, content.ToString())];
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
        => await documentLoader.LoadByEntryIdsAsync([entry.Id], cancellationToken).ConfigureAwait(false);
}
