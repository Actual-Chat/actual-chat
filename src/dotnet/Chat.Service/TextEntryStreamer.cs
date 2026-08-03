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
    private MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    protected ILogger Log => field ??= Services.LogFor(GetType());

    public virtual Task<ChatEntry> Stream(
        ChatId chatId,
        AuthorId authorId,
        IAsyncEnumerable<string> textChunks,
        CancellationToken cancellationToken = default)
        => Stream(chatId, authorId, null, textChunks, cancellationToken);

    public virtual async Task<ChatEntry> Stream(
        ChatId chatId,
        AuthorId authorId,
        ChatEntry? entryToUpdate,
        IAsyncEnumerable<string> textChunks,
        CancellationToken cancellationToken = default)
    {
        var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
        using var stream = ToTranscriptDiffs(textChunks, cancellationToken)
            .Memoize(cancellationToken);
        var rpcStream = RpcStream.New(stream.Replay(cancellationToken));
        var publishStreamTask = StreamingBackend.PushTranscript(streamId, rpcStream, cancellationToken);

        // The entry has to exist before the stream is drained: readers find the stream through
        // its ContentStreamId, so anything published earlier has no subscriber to reach.
        var entry = entryToUpdate is null
            ? await CreateEntry().ConfigureAwait(false)
            : await StartStreamingInto(entryToUpdate).ConfigureAwait(false);
        var transcript = Transcript.Empty;
        try {
            await foreach (var diff in stream.Replay(cancellationToken).ConfigureAwait(false))
                transcript = diff.ApplyTo(transcript);
            await publishStreamTask.ConfigureAwait(false);
        }
        finally {
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

    private async IAsyncEnumerable<TranscriptDiff> ToTranscriptDiffs(
        IAsyncEnumerable<string> textChunks,
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

            yield return new TranscriptDiff(StringDiff.New(newText, text), LinearMapDiff.None) {
                IsStable = true,
            };

            text = newText;
        }
    }
}
