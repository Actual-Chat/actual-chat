using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Mesh;
using ActualChat.Streaming;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Posts a text entry whose content arrives over time: the entry appears immediately with an
/// empty body and a content stream readers can subscribe to, and is finalized with the full text.
/// </summary>
public class TextEntryStreamer(IServiceProvider services)
{
    // 5 outbound updates per second at most, regardless of how fast chunks arrive
    private static readonly TimeSpan OutboundUpdateInterval = TimeSpan.FromMilliseconds(200);

    private IServiceProvider Services { get; } = services;

    private ICommander Commander => field ??= Services.Commander();
    private IAudioStreamingBackend StreamingBackend => field ??= Services.GetRequiredService<IAudioStreamingBackend>();
    private ILiveAudioBackend LiveAudioBackend => field ??= Services.GetRequiredService<ILiveAudioBackend>();
    private ILiveSessionsBackend LiveSessionsBackend
        => field ??= Services.GetRequiredService<ILiveSessionsBackend>();
    private MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    protected ILogger Log => field ??= Services.LogFor(GetType());

    public virtual Task<ChatEntry> Stream(
        ChatId chatId,
        AuthorId authorId,
        IAsyncEnumerable<string> textChunks,
        CancellationToken cancellationToken = default)
        => Stream(chatId, authorId, null, textChunks, cancellationToken);

    // entryCreatedSource fires as soon as the entry exists, which is long before this method
    // returns - a lease-driven producer needs the entry id to hand back from its "start" call.
    public virtual async Task<ChatEntry> Stream(
        ChatId chatId,
        AuthorId authorId,
        ChatEntry? entryToUpdate,
        IAsyncEnumerable<string> textChunks,
        CancellationToken cancellationToken = default,
        bool? isViaApi = null,
        TaskCompletionSource<ChatEntry>? entryCreatedSource = null,
        Language? language = null)
    {
        var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
        using var stream = ToTranscriptDiffs(textChunks, language, cancellationToken)
            .Memoize(cancellationToken);
        var rpcStream = RpcStream.New(stream.Replay(cancellationToken));
        // PushTextTranscript rather than PushTranscript: nothing else registers the speaker for a
        // transcript with no audio, and without it the text cannot be spoken aloud later.
        var publishStreamTask = StreamingBackend.PushTextTranscript(
            streamId, chatId, authorId, rpcStream, cancellationToken);

        // The entry has to exist before the stream is drained: readers find the stream through
        // its ContentStreamId, so anything published earlier has no subscriber to reach.
        ChatEntry entry;
        try {
            entry = entryToUpdate is null
                ? await CreateEntry().ConfigureAwait(false)
                : await StartStreamingInto(entryToUpdate).ConfigureAwait(false);
        }
        catch (Exception e) {
            entryCreatedSource?.TrySetException(e);
            throw;
        }
        entryCreatedSource?.TrySetResult(entry);

        // Announce it as live audio so a listener's client discovers something to play: that
        // request is what makes the server speak the text. Without it nothing ever asks, and a
        // bot is silent to someone who is listening to everyone else in the room.
        var beginsAt = Clocks.ServerClock.Now;
        await RegisterSpeech(entry, beginsAt, language).ConfigureAwait(false);

        var transcript = Transcript.Empty;
        try {
            await foreach (var diff in stream.Replay(cancellationToken).ConfigureAwait(false))
                transcript = diff.ApplyTo(transcript);
            await publishStreamTask.ConfigureAwait(false);
        }
        finally {
            await LiveAudioBackend
                .Unregister(chatId, streamId.Value, CancellationToken.None)
                .SilentAwait(false);
            // A recording client clears its own participation when it stops; nothing here holds a
            // microphone, so the chat would keep reporting a live speaker for the whole staleness
            // window after the last word.
            await LiveSessionsBackend
                .SetParticipation(chatId, authorId, ParticipationKind.Record, false, CancellationToken.None)
                .SilentAwait(false);
            // Finalized even on failure, otherwise the entry stays empty and streaming forever.
            entry = await FinalizeEntry(entry, transcript.Text).ConfigureAwait(false);
        }
        return entry;

        Task<ChatEntry> CreateEntry() {
            var diff = new ChatEntryDiff {
                AuthorId = authorId,
                Content = "",
                ContentStreamId = streamId.Value,
                BeginsAt = Clocks.SystemClock.Now,
                IsViaApi = isViaApi,
            };
            var command = new ChatsBackend_ChangeEntry(
                ChatEntryId.New(chatId, 0),
                null,
                Change.Create(diff));
            return Commander.Call(command, true, cancellationToken);
        }

        Task<ChatEntry> StartStreamingInto(ChatEntry entry1) {
            // Content is left as it was: until the stream finalizes, a reader that doesn't follow
            // it still sees the previous text rather than an empty message.
            var diff = new ChatEntryDiff {
                ContentStreamId = streamId.Value,
                IsViaApi = isViaApi,
            };
            var command = new ChatsBackend_ChangeEntry(entry1.Id, null, Change.Update(diff));
            return Commander.Call(command, true, cancellationToken);
        }

        Task<ChatEntry> FinalizeEntry(ChatEntry entryToFinalize, string text) {
            var diff = new ChatEntryDiff {
                Content = text,
                ContentStreamId = "",
                EndsAt = Clocks.SystemClock.Now,
            };
            var command = new ChatsBackend_ChangeEntry(
                entryToFinalize.Id,
                null, // No version check: the entry may have changed while streaming, and that's fine
                Change.Update(diff));
            return Commander.Call(command, true, CancellationToken.None);
        }
    }

    // Private methods

    private async Task RegisterSpeech(ChatEntry entry, Moment beginsAt, Language? language)
    {
        // IsTextOnly false on purpose: there is no recording, but there is audio to be had - the
        // synthesis - and GetStream refuses to serve a stream marked text-only.
        var streamInfo = new LiveAudioStreamInfo {
            ChatId = entry.ChatId,
            AuthorId = entry.AuthorId,
            StreamId = entry.ContentStreamId,
            BeginsAt = beginsAt,
            SourceBeginsAt = beginsAt,
            Format = AudioSource.DefaultFormat,
            IsTextOnly = false,
            // Empty would mean "never dub" - and the dub request is exactly what makes the
            // server speak this. Declaring the language is what makes a listener ask.
            Languages = language is { } l ? new ApiArray<Language>([l]) : ApiArray<Language>.Empty,
        };
        await LiveAudioBackend.Register(entry.ChatId, streamInfo, CancellationToken.None).ConfigureAwait(false);
        // Registering the audio only makes it discoverable to someone already listening. The session
        // is what puts activity on the chat and offers a Join, so a bot speaking to a room where
        // nobody happens to be listening can still be heard by someone who walks in.
        await LiveSessionsBackend
            .OnStreamRegistered(entry.ChatId, entry.AuthorId, entry.LocalId, true, true, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async IAsyncEnumerable<TranscriptDiff> ToTranscriptDiffs(
        IAsyncEnumerable<string> textChunks,
        Language? language,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Producers may push at any rate - an LLM emits a token at a time - but every diff costs a
        // fan-out to every reader, so a window's worth of chunks is coalesced into one update.
        var text = "";
        var batches = textChunks
            .Buffer(OutboundUpdateInterval, Clocks.CpuClock, cancellationToken: cancellationToken);
        await foreach (var batch in batches.ConfigureAwait(false)) {
            if (batch.Count == 0)
                continue;

            var newText = text + string.Concat(batch);
            if (newText == text)
                continue;

            // The producer declares its language, so a transcript with no audio still says what
            // it is - which is what lets it be spoken, and translated, later
            yield return new TranscriptDiff(StringDiff.New(newText, text), LinearMapDiff.None) {
                IsStable = true,
                Languages = language is { } l ? [l] : null,
            };

            text = newText;
        }
    }
}
