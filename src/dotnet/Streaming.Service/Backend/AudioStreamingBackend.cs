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
    public virtual async Task<Transcript?> GetMergedTranscript(StreamId streamId, CancellationToken cancellationToken)
    {
        // waitForShare: false, so a stream that hasn't been published yet doesn't block the compute
        // for ShareWaitDelay. Nothing invalidates that null - the entry doesn't exist to depend on -
        // so it has to re-check itself, otherwise an observer that captured this a moment too early
        // stays pinned to null for the whole stream.
        var computed = Computed.GetCurrent();
        var memoizer = await _transcriptStreams
            .GetMemoizer(streamId, false, cancellationToken)
            .ConfigureAwait(false);
        if (memoizer == null) {
            computed.Invalidate(AudioSettings.MergedTranscriptRetryDelay);
            return null;
        }

        var (transcript, producedCount) = memoizer.FoldBuffered(
            Transcript.Empty,
            static (t, diff) => t + diff);
        if (memoizer.IsCompleted)
            return transcript;

        _ = memoizer.WhenChanged(producedCount)
            .ContinueWith(
                _ => computed.Invalidate(AudioSettings.MergedTranscriptInvalidationDelay),
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
            var memoizer = ((IAsyncEnumerable<TranscriptDiff>)diffStream).Memoize(cancellationToken);
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

    // Private methods

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
