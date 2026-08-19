using ActualChat.Audio;
using ActualChat.Diagnostics;
using ActualChat.Streaming.Services;
using ActualChat.Streaming.Module;
using ActualChat.Transcription;
using ActualLab.Rpc;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Streaming;

/// <summary>
/// Backend service implementation for audio and transcript streaming with real-time transcription.
/// </summary>
public partial class AudioStreamingBackend : IAudioStreamingBackend, IDisposable
{
    // A late subscriber wants the transcript, not the keystroke-by-keystroke history that built it,
    // so the memoized diff stream folds its buffered prefix into one diff off Transcript.Empty.
    internal static readonly Func<Transcript, TranscriptDiff, Transcript> TranscriptFolder
        = static (transcript, diff) => transcript + diff;
    internal static readonly Func<Transcript, TranscriptDiff> TranscriptToDiff
        = static transcript => transcript - Transcript.Empty;

    private readonly StreamStore<AudioFrame> _audioStreams;
    private readonly StreamStore<TranscriptDiff> _transcriptStreams;
    private readonly ConcurrentDictionary<StreamId, StreamId> _translatingStreams = new();
    private readonly ConcurrentDictionary<StreamId, ChatId> _chatIdByStream = new();

    private ILogger Log => field ??= Services.LogFor(GetType());
    private ILogger OpenAudioSegmentLog => field ??= Services.LogFor<OpenAudioSegment>();
    private ILogger AudioSourceLog => field ??= Services.LogFor<AudioSource>();
    private static bool DebugMode => Constants.DebugMode.AudioProcessor;
    private ILogger? DebugLog => DebugMode ? Log : null;

    private IServiceProvider Services { get; }
    private MeshNode ThisNode => field ??= Services.MeshWatcher().ThisNode;
    private AudioSettings AudioSettings { get; }
    private StreamingSettings StreamingSettings { get; }
    private AudioSegmentSaver AudioSegmentSaver => field ??= Services.GetRequiredService<AudioSegmentSaver>();
    private ILiveAudioBackend LiveAudioBackend => field ??= Services.GetRequiredService<ILiveAudioBackend>();
    private ILiveSessionsBackend LiveSessionsBackend => field ??= Services.GetRequiredService<ILiveSessionsBackend>();
    private ITranscriberSelector TranscriberSelector => field ??= Services.GetRequiredService<ITranscriberSelector>();
    private ITranscriberRegistry TranscriberRegistry => field ??= Services.GetRequiredService<ITranscriberRegistry>();
    private ITranscriptionContextSource? TranscriptionContextSource
        // Optional: it lives in Chat.Service, which isn't loaded in every host or test.
        => field ??= Services.GetService<ITranscriptionContextSource>();
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private ICommander Commander => field ??= Services.Commander();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private IHostApplicationLifetime HostLifetime => field ??= Services.HostLifetime();

    public AudioStreamingBackend(IServiceProvider services)
    {
        Services = services;
        AudioSettings = services.GetRequiredService<AudioSettings>();
        StreamingSettings = services.GetRequiredService<StreamingSettings>();

        var typeFullName = GetType().FullName;
        _audioStreams = new StreamStore<AudioFrame> {
            StreamIdValidator = ValidateStreamId,
            StreamCount = AppMeters.AudioStreamCount,
            ExpirationDelay = AudioSettings.StreamExpirationDelay,
            OnStreamExpire = ForgetChatIdIfUnused,
            Log = services.LogFor($"{typeFullName}.AudioStreams"),
        };
        _transcriptStreams = new StreamStore<TranscriptDiff> {
            StreamIdValidator = ValidateStreamId,
            ExpirationDelay = AudioSettings.StreamExpirationDelay,
            OnStreamExpire = id => {
                _translatingStreams.Remove(id, out _);
                ForgetChatIdIfUnused(id);
            },
            Log = services.LogFor($"{typeFullName}.TranscriptStreams"),
        };
    }

    public void Dispose()
    {
        _audioStreams.Dispose();
        _transcriptStreams.Dispose();
    }

    public virtual Task<ChatId?> GetChatId(StreamId streamId, CancellationToken cancellationToken)
        => Task.FromResult(_chatIdByStream.GetValueOrDefault(BaseStreamId(streamId)));

    public virtual async Task<RpcStream<AudioFrame>?> GetAudio(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        if (skipTo == Constants.Audio.SkipToLive) {
            var memoizer = await _audioStreams.GetMemoizer(streamId, true, cancellationToken).ConfigureAwait(false);
            return memoizer == null
                ? null
                : StandardRpcStream.NewAudioDelivery(SkipToLive(memoizer, cancellationToken));
        }

        var stream = await _audioStreams.Get(streamId, cancellationToken).ConfigureAwait(false);
        if (stream == null)
            return null;

        stream = SkipTo(stream, skipTo, cancellationToken);
        return StandardRpcStream.NewAudioDelivery(stream);
    }

    public virtual async Task<RpcStream<TranscriptDiff>?> GetTranscript(
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("GetTranscript: #{StreamId}", streamId);
        var stream = await _transcriptStreams.Get(streamId, false, cancellationToken).ConfigureAwait(false);
        if (stream != null)
            return StandardRpcStream.NewTranscriptDelivery(stream);

        var language = streamId.Language;
        if (language == null)
            return null;

        var originalStreamId = StreamId.New(streamId.NodeRef, streamId.LocalId);
        if (!_translatingStreams.TryAdd(streamId, originalStreamId)) {
            stream = await _transcriptStreams.Get(streamId, true, cancellationToken).ConfigureAwait(false);
            return StandardRpcStream.NewTranscriptDelivery(stream!); // Already translating
        }

        DebugLog?.LogDebug("GetTranscript: #{StreamId} - Translate stream", streamId);

        var cmd = new TranslationsBackend_TranslateStream(originalStreamId, language);
        // Use ApplicationStopping as GetTranscript might be canceled, but we still want to wait
        // for the translated stream to be created.
        await Commander.Call(cmd, HostLifetime.StopToken()).ConfigureAwait(false);
        stream = await _transcriptStreams.Get(streamId, true, cancellationToken).ConfigureAwait(false);

        DebugLog?.LogDebug("GetTranscript: #{StreamId} - Return stream", streamId);
        return stream == null
            ? null
            : StandardRpcStream.NewTranscriptDelivery(stream);
    }

    // [ComputeMethod]
    public virtual async Task<Transcript?> GetTranscriptSnapshot(StreamId streamId, CancellationToken cancellationToken)
    {
        // waitForShare: false keeps an unpublished stream from blocking the compute for
        // ShareWaitDelay - but nothing invalidates that null, since there's no entry to depend on,
        // so it re-checks itself. Without that, an observer that captured a moment too early stays
        // pinned to null for the whole stream.
        var computed = Computed.GetCurrent();
        var memoizer = await _transcriptStreams
            .GetMemoizer(streamId, false, cancellationToken)
            .ConfigureAwait(false);
        if (memoizer == null) {
            computed.Invalidate(AudioSettings.TranscriptSnapshotRetryDelay);
            return null;
        }

        // Folding memoizer resumes from its checkpoint; the fallback only runs if some future
        // publisher forgets to use MemoizeFolding, and costs a full refold per read.
        var (transcript, producedCount) = memoizer is FoldingAsyncMemoizer<TranscriptDiff, Transcript> folding
            ? folding.Fold()
            : memoizer.FoldBuffered(Transcript.Empty, TranscriptFolder);
        if (memoizer.IsCompleted)
            return transcript;

        _ = memoizer.WhenChanged(producedCount)
            .ContinueWith(
                _ => computed.Invalidate(AudioSettings.TranscriptSnapshotInvalidationDelay),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        return transcript;
    }

    public async Task PushTranscript(
        StreamId streamId,
        RpcStream<TranscriptDiff> diffStream,
        CancellationToken cancellationToken)
    {
        try {
            ValidateStreamId(streamId);
            var memoizer = ((IAsyncEnumerable<TranscriptDiff>)diffStream)
                .MemoizeFolding(Transcript.Empty, TranscriptFolder, TranscriptToDiff, cancellationToken);
            if (_transcriptStreams.Publish(streamId, memoizer))
                await (memoizer.WhenRunning ?? Task.CompletedTask).ConfigureAwait(false);
            else
                await memoizer.DisposeAsync().ConfigureAwait(false);
        }
        finally {
            // Release the producer's sender — see PushAudio/PushVideo.
            diffStream.Disconnect();
        }
    }

    // Protected/internal methods

    internal void RememberChatId(StreamId streamId, ChatId chatId)
        => _chatIdByStream[BaseStreamId(streamId)] = chatId;

    internal static IAsyncEnumerable<AudioFrame> SkipTo(
        IAsyncEnumerable<AudioFrame> stream,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        if (skipTo <= TimeSpan.Zero)
            return stream;

        // Preserve the stream header and original frame offsets while trimming
        // stale data frames. The client still sees source-time offsets for the
        // frames that remain and can perform fine playback/A-V catch-up locally.
        var (headerTask, dataStream) = stream.SplitHead(cancellationToken);
        return dataStream
            .SkipWhile(f => f.Offset < skipTo)
            .PrependOne(headerTask);
    }

    internal static IAsyncEnumerable<AudioFrame> SkipToLive(
        AsyncMemoizer<AudioFrame> memoizer,
        CancellationToken cancellationToken)
    {
        // Pinning the tail here rather than inside the iterator makes the live edge the
        // moment of the request, not the moment the consumer starts enumerating.
        // SplitHead is deliberately not used: it pumps its entire source into an unbounded
        // channel, which is right when the tail it makes is the returned stream, but here
        // that tail would be dropped and the pump would buffer a live stream forever.
        var tail = memoizer.Replay(0, cancellationToken);
        return WithHeader(memoizer, tail, cancellationToken);
    }

    // Private methods

    private static async IAsyncEnumerable<AudioFrame> WithHeader(
        AsyncMemoizer<AudioFrame> memoizer,
        IAsyncEnumerable<AudioFrame> tail,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var header = await ReadHeader(memoizer, cancellationToken).ConfigureAwait(false);
        if (header != null)
            yield return header;

        // Replay(0) starts at the memoizer's tail, so it yields only future frames - except
        // when nothing was buffered yet, where the tail is still the sentinel and the header
        // arrives as a "future" frame. The offset filter is what keeps it from being emitted twice.
        await foreach (var frame in tail.WithCancellation(cancellationToken).ConfigureAwait(false))
            if (frame.Offset >= TimeSpan.Zero)
                yield return frame;
    }

    private static async Task<AudioFrame?> ReadHeader(
        AsyncMemoizer<AudioFrame> memoizer,
        CancellationToken cancellationToken)
    {
        await using var enumerator = memoizer
            .Replay(int.MaxValue, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        return await enumerator.MoveNextAsync().ConfigureAwait(false)
            ? enumerator.Current
            : null;
    }

    private void ForgetChatIdIfUnused(StreamId streamId)
    {
        // ExpiringEntry self-removes before calling this, so Has already excludes it.
        var baseStreamId = BaseStreamId(streamId);
        if (!_audioStreams.Has(baseStreamId) && !_transcriptStreams.Has(baseStreamId))
            _chatIdByStream.TryRemove(baseStreamId, out _);
    }

    private static StreamId BaseStreamId(StreamId streamId)
        => streamId.Language == null ? streamId : StreamId.New(streamId.NodeRef, streamId.LocalId);

    private void ValidateStreamId(StreamId streamId)
    {
        if (streamId.NodeRef != ThisNode.Ref)
            throw new ArgumentOutOfRangeException(nameof(streamId),
                $"Wrong mesh node: expected {ThisNode.Ref}, but got {streamId.NodeRef}.");
    }
}
