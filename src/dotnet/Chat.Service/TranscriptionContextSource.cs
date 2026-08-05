using ActualChat.Streaming;
using ActualChat.Transcription;

namespace ActualChat.Chat;

// Deliberately over-provisions: it captures more entries and authors than any transcriber's budget
// can use, and TranscriptionContext.Render trims per policy. That keeps this result shareable across
// transcribers whose budgets differ - and across every speaker in a chat, which matters most during
// calls, where a new transcription stream starts every few seconds.

public sealed class TranscriptionContextSource(IServiceProvider services) : ITranscriptionContextSource
{
    private const int MaxScannedEntries = 20;
    private const int MaxPrefixEntries = 8;
    private const int MaxAuthors = 8;
    private IServiceProvider Services { get; } = services;
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private ILiveSessionsBackend LiveSessions => field ??= Services.GetRequiredService<ILiveSessionsBackend>();
    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory
        => field ??= Services.KeyedFactory<IBackendChatMarkupHub, ChatId>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<TranscriptionContext> GetContext(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.Value.IsNullOrEmpty())
            return TranscriptionContext.None;

        try {
            var summary = await GetSummary(chatId, cancellationToken).ConfigureAwait(false);
            var entries = await ListRecentEntries(chatId, cancellationToken).ConfigureAwait(false);
            var (prefix, authors) = await BuildPrefix(chatId, entries, cancellationToken).ConfigureAwait(false);
            return new TranscriptionContext {
                Summary = summary,
                Prefix = prefix,
                Authors = authors,
            };
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            // Context only improves recognition, so a failure here must not fail transcription.
            Log.LogWarning(e, "Failed to build transcription context for #{ChatId}", chatId);
            return TranscriptionContext.None;
        }
    }

    // Private methods

    private async Task<string> GetSummary(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await LiveSessions.GetState(chatId, cancellationToken).ConfigureAwait(false);
        // An ongoing call describes the topic far better than the chat's static title.
        if (state != null && !state.Summary.IsNullOrEmpty())
            return state.Summary;
        if (state != null && !state.Title.IsNullOrEmpty())
            return state.Title;

        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        return chat?.Title ?? "";
    }

    private async Task<IReadOnlyList<ChatEntry>> ListRecentEntries(ChatId chatId, CancellationToken cancellationToken)
    {
        var lidRange = await ChatsBackend.GetLidRange(chatId, true, cancellationToken).ConfigureAwait(false);
        if (lidRange.Size() <= 0)
            return [];

        var minLid = Math.Max(lidRange.Start, lidRange.End - MaxScannedEntries);
        var entries = await ChatsBackend
            .ListEntries(chatId, new Range<long>(minLid, lidRange.End), false, cancellationToken)
            .ConfigureAwait(false);
        return entries
            .Where(x => !x.IsSystemEntry && !x.IsContentStreaming && !x.Content.IsNullOrEmpty())
            .OrderBy(x => x.LocalId)
            .ToList();
    }

    private async Task<(ApiArray<TranscriptionContextEntry>, ApiArray<TranscriptionAuthor>)> BuildPrefix(
        ChatId chatId,
        IReadOnlyList<ChatEntry> entries,
        CancellationToken cancellationToken)
    {
        var chatMarkupHub = ChatMarkupHubFactory[chatId];
        var prefix = new List<TranscriptionContextEntry>(MaxPrefixEntries);
        var authorLocalIds = new List<long>(MaxAuthors);
        foreach (var entry in entries.TakeLast(MaxPrefixEntries)) {
            var markup = await chatMarkupHub.GetMarkup(entry, MarkupConsumer.Notification, cancellationToken)
                .ConfigureAwait(false);
            // ReadableUnstyled keeps mentions as "@Name" instead of "@a:<chatId>:<localId>".
            var text = markup.ToReadableText(MarkupFormatter.ReadableUnstyled);
            if (text.IsNullOrEmpty())
                continue;

            var authorLocalId = entry.AuthorId.LocalId;
            prefix.Add(new TranscriptionContextEntry(authorLocalId, text));
            if (!authorLocalIds.Contains(authorLocalId))
                authorLocalIds.Add(authorLocalId);
        }

        var authors = new List<TranscriptionAuthor>(authorLocalIds.Count);
        foreach (var authorLocalId in authorLocalIds.TakeLast(MaxAuthors)) {
            var authorId = AuthorId.New(chatId, authorLocalId);
            var author = await AuthorsBackend
                .Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken)
                .ConfigureAwait(false);
            var name = author?.Avatar.Name ?? "";
            if (!name.IsNullOrEmpty())
                authors.Add(new TranscriptionAuthor(authorLocalId, name));
        }

        return (prefix.ToApiArray(), authors.ToApiArray());
    }
}
