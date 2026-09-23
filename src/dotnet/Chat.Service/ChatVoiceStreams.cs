using System.Threading.Channels;
using ActualChat.Audio;
using ActualChat.Audio.Ogg;
using ActualChat.Mesh;
using ActualChat.Streaming;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Holds the open producer side of every call-driven voice entry this node started, keyed by a
/// handle whose <see cref="StreamId.NodeRef"/> routes later calls back here.
/// </summary>
public class ChatVoiceStreams(IServiceProvider services) : IChatVoiceStreamsBackend, IDisposable
{
    private readonly ConcurrentDictionary<Symbol, ExpiringEntry<Symbol, Lease>> _leases = new();

    private IServiceProvider Services { get; } = services;
    private IAudioStreamingBackend StreamingBackend
        => field ??= Services.GetRequiredService<IAudioStreamingBackend>();
    private IMaintenancesBackend Maintenances => field ??= Services.GetRequiredService<IMaintenancesBackend>();
    private MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILogger Log => field ??= Services.LogFor(GetType());

    // Settable so tests don't have to wait out the real timeout
    public TimeSpan IdleTimeout { get; set; } = Constants.Chat.EntryStreamIdleTimeout;

    public void Dispose()
    {
        foreach (var expiringLease in _leases.Values.ToList())
            expiringLease.Dispose();
    }

    public virtual Task<ChatVoiceStream> Start(
        ChatId chatId,
        Session session,
        UserId userId,
        long? repliedEntryLid,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
        var repliedEntryId = repliedEntryLid is { } lid ? ChatEntryId.New(chatId, lid) : (ChatEntryId?)null;
        var record = new AudioRecord(
            streamId, session, chatId, Clocks.SystemClock.Now.EpochOffset.TotalSeconds, repliedEntryId);
        var lease = new Lease(streamId, userId, chatId, record);
        var expiringLease = ExpiringEntry
            .New(_leases, streamId.Value, lease)
            .SetDisposer(e => {
                if (!e.Value.IsCompleted)
                    Log.LogWarning("Voice stream #{StreamId} was abandoned - finalizing", e.Value.Id);
                e.Value.Dispose();
            });
        if (!_leases.TryAdd(streamId.Value, expiringLease)) {
            lease.Dispose();
            throw StandardError.Internal("Duplicate voice stream id.");
        }

        expiringLease.BumpExpiresAt(IdleTimeout).BeginExpire();
        return Task.FromResult(lease.ToModel());
    }

    public virtual Task<ChatVoiceStream> Append(
        StreamId streamId,
        UserId userId,
        int textOffset,
        string? text,
        byte[]? audio,
        double? audioOffset,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var expiringLease = GetOwnLease(streamId, userId);
        var lease = expiringLease.Value;
        lock (lease.Lock) {
            if (lease.IsCompleted)
                throw StandardError.Constraint("This voice stream is already finished.");

            if (audio is { Length: > 0 }) {
                AppendAudio(lease, audio);
                // The header is in by now, so preSkip is known and processing can start - which is
                // what makes listeners hear the stream while it is still arriving
                if (lease.OggReader.Head is not null)
                    EnsureStarted(lease);
            }
            if (!text.IsNullOrEmpty())
                AppendText(lease, textOffset, text, audioOffset);
        }

        expiringLease.BumpExpiresAt(IdleTimeout);
        return Task.FromResult(lease.ToModel());
    }

    public virtual async Task<ChatVoiceStream> Finish(
        StreamId streamId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var expiringLease = GetOwnLease(streamId, userId);
        var lease = expiringLease.Value;
        lock (lease.Lock) {
            if (!lease.IsCompleted)
                EnsureStarted(lease);
        }
        lease.Frames.Writer.TryComplete();
        lease.Chunks.Writer.TryComplete();
        await (lease.StreamTask ?? Task.CompletedTask).WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (lease.Lock)
            lease.IsCompleted = true;

        // Kept for the idle timeout rather than dropped, so a retried finish answers with the
        // same result instead of "unknown stream".
        expiringLease.BumpExpiresAt(IdleTimeout);
        return lease.ToModel();
    }

    // Private methods

    private static void AppendAudio(Lease lease, byte[] audio)
    {
        if (audio.Length > Constants.Chat.MaxVoiceStreamChunkBytes)
            throw StandardError.Constraint("Audio chunk is too large.");
        if (lease.AudioBytes + audio.Length > Constants.Chat.MaxVoiceStreamAudioBytes)
            throw StandardError.Constraint("This voice stream is too long.");

        // Append/TryRead keeps a frame that straddles two chunks: the reader buffers the partial
        // packet until the bytes completing it arrive, which is why a producer may chunk anywhere
        lease.OggReader.Append(audio);
        while (lease.OggReader.TryRead(out var frame))
            lease.Frames.Writer.TryWrite(frame);
        lease.AudioBytes += audio.Length;
    }

    private static void AppendText(Lease lease, int textOffset, string text, double? audioOffset)
    {
        // Same contract as the text lease: a mismatched offset writes nothing and reports where
        // the server is, so a client that retried or lost a response can resume.
        if (textOffset != lease.TextOffset)
            return;
        if (lease.TextOffset + text.Length > Constants.Chat.MaxEntryTextLength)
            throw StandardError.Constraint(
                $"A message can hold up to {Constants.Chat.MaxEntryTextLength} characters.");

        lease.Chunks.Writer.TryWrite(new ExternalTranscriptChunk(text, true, audioOffset, true));
        lease.TextOffset += text.Length;
    }

    private void EnsureStarted(Lease lease)
    {
        // Deferred until the first audio arrives: ProcessAudioWithTranscript needs preSkip up
        // front, and only the Ogg header carries it. Text sent before any audio waits in its
        // channel, which is what a producer writing a clause before speaking it does.
        if (lease.StreamTask is not null)
            return;

        var chunks = lease.Chunks.Reader
            .ReadAllAsync(lease.StopTokenSource.Token)
            .RequireAvailable(Maintenances, lease.ChatId, lease.StopTokenSource.Token);
        lease.StreamTask = StreamingBackend.ProcessAudioWithTranscript(
            lease.Record,
            lease.OggReader.PreSkip,
            RpcStream.New(lease.Frames.Reader.ReadAllAsync(lease.StopTokenSource.Token)),
            RpcStream.New(chunks),
            lease.StopTokenSource.Token);
    }

    private ExpiringEntry<Symbol, Lease> GetOwnLease(StreamId streamId, UserId userId)
    {
        if (!_leases.TryGetValue(streamId.Value, out var expiringLease))
            throw StandardError.NotFound<ChatVoiceStream>("This voice stream is unknown or has expired.");
        if (expiringLease.Value.OwnerId != userId)
            throw StandardError.Unauthorized("You can write only to your own voice streams.");

        return expiringLease;
    }

    // Nested types

    private sealed class Lease(StreamId id, UserId ownerId, ChatId chatId, AudioRecord record) : IDisposable
    {
        public object Lock { get; } = new();
        public StreamId Id { get; } = id;
        public UserId OwnerId { get; } = ownerId;
        public ChatId ChatId { get; } = chatId;
        public AudioRecord Record { get; } = record;
        public OggOpusReader OggReader { get; } = new();
        public Channel<AudioFrame> Frames { get; } = Channel.CreateUnbounded<AudioFrame>(
            new UnboundedChannelOptions { SingleReader = true });
        public Channel<ExternalTranscriptChunk> Chunks { get; }
            = Channel.CreateUnbounded<ExternalTranscriptChunk>(new UnboundedChannelOptions { SingleReader = true });
        public CancellationTokenSource StopTokenSource { get; } = new(Constants.Chat.MaxEntryStreamDuration);

        public Task? StreamTask { get; set; }
        public ChatEntryId? EntryId { get; set; }
        public int TextOffset { get; set; }
        public long AudioBytes { get; set; }
        public bool IsCompleted { get; set; }

        public void Dispose()
        {
            // Completing both channels is what makes the audio pipeline finalize the entry, so an
            // abandoned stream leaves a playable message rather than one that streams forever.
            Frames.Writer.TryComplete();
            Chunks.Writer.TryComplete();
            _ = (StreamTask ?? Task.CompletedTask).ContinueWith(
                (_, state) => ((CancellationTokenSource)state!).DisposeSilently(),
                StopTokenSource,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public ChatVoiceStream ToModel()
        {
            lock (Lock)
                return new ChatVoiceStream(Id, EntryId, TextOffset, AudioBytes, IsCompleted);
        }
    }
}
