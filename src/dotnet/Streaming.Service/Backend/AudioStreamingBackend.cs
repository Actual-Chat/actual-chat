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
    private readonly Dictionary<StreamId, int> _translationReaderCounts = new();
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
                lock (_translationReaderCounts)
                    _translationReaderCounts.Remove(id);
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
        if (streamId.Language == null) {
            var stream = await _transcriptStreams.Get(streamId, false, cancellationToken).ConfigureAwait(false);
            return stream == null ? null : StandardRpcStream.NewTranscriptDelivery(stream);
        }

        // Counted before the lookup: a reader that isn't counted yet can't have found a stream
        // StopIdleTranslation is about to drop.
        AddTranslationReader(streamId);
        IAsyncEnumerable<TranscriptDiff>? translatedStream = null;
        try {
            translatedStream = await GetTranslatedTranscript(streamId, cancellationToken).ConfigureAwait(false);
        }
        finally {
            if (translatedStream == null)
                RemoveTranslationReader(streamId);
        }
        return translatedStream == null
            ? null
            : StandardRpcStream.NewTranscriptDelivery(CountReader(streamId, translatedStream, cancellationToken));
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

    private async Task<IAsyncEnumerable<TranscriptDiff>?> GetTranslatedTranscript(
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        var stream = await _transcriptStreams.Get(streamId, false, cancellationToken).ConfigureAwait(false);
        if (stream != null)
            return stream;

        var originalStreamId = StreamId.New(streamId.NodeRef, streamId.LocalId);
        if (!_translatingStreams.TryAdd(streamId, originalStreamId)) // Already translating
            return await _transcriptStreams.Get(streamId, true, cancellationToken).ConfigureAwait(false);

        DebugLog?.LogDebug("GetTranscript: #{StreamId} - Translate stream", streamId);

        var cmd = new TranslationsBackend_TranslateStream(originalStreamId, streamId.Language!);
        // Use ApplicationStopping as GetTranscript might be canceled, but we still want to wait
        // for the translated stream to be created.
        await Commander.Call(cmd, HostLifetime.StopToken()).ConfigureAwait(false);
        stream = await _transcriptStreams.Get(streamId, true, cancellationToken).ConfigureAwait(false);

        DebugLog?.LogDebug("GetTranscript: #{StreamId} - Return stream", streamId);
        return stream;
    }

    private async IAsyncEnumerable<TranscriptDiff> CountReader(
        StreamId streamId,
        IAsyncEnumerable<TranscriptDiff> stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try {
            await foreach (var diff in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return diff;
        }
        finally {
            RemoveTranslationReader(streamId);
        }
    }

    private void AddTranslationReader(StreamId streamId)
    {
        lock (_translationReaderCounts)
            _translationReaderCounts[streamId] = _translationReaderCounts.GetValueOrDefault(streamId) + 1;
    }

    private void RemoveTranslationReader(StreamId streamId)
    {
        lock (_translationReaderCounts) {
            var count = _translationReaderCounts.GetValueOrDefault(streamId) - 1;
            if (count > 0) {
                _translationReaderCounts[streamId] = count;
                return;
            }

            _translationReaderCounts.Remove(streamId);
        }
        _ = BackgroundTask.Run(() => StopIdleTranslation(streamId), Log, $"{nameof(StopIdleTranslation)} failed");
    }

    private async Task StopIdleTranslation(StreamId streamId)
    {
        // A translated transcript costs an LLM call per batch of diffs, so unlike its source it
        // isn't kept running for nobody: dropping the stream ends the push feeding it, which stops
        // the translator, and the next reader starts it over - see TranslationsBackend.
        var idleTimeout = AudioSettings.TranslatedTranscriptIdleTimeout;
        await Clocks.CpuClock.Delay(idleTimeout, CancellationToken.None).ConfigureAwait(false);
        var memoizer = await _transcriptStreams
            .GetMemoizer(streamId, false, CancellationToken.None)
            .ConfigureAwait(false);
        if (memoizer == null || memoizer.IsCompleted)
            return;

        lock (_translationReaderCounts) {
            if (_translationReaderCounts.ContainsKey(streamId) || !_transcriptStreams.TryRemove(streamId))
                return;
        }

        Log.LogInformation("Translation #{StreamId} stopped: no readers for {IdleTimeout}s",
            streamId, idleTimeout.TotalSeconds);
    }

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
            .ConfigureAwait(false)
            .GetAsyncEnumerator();
        return await enumerator.MoveNextAsync()
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
