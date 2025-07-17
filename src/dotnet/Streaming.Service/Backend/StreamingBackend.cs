using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Diagnostics;
using ActualChat.Kvas;
using ActualChat.Mesh;
using ActualChat.Queues;
using ActualChat.Streaming.Services;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public partial class StreamingBackend : IStreamingBackend, IDisposable
{
    private readonly StreamStore<byte[]> _audioStreams;
    private readonly StreamStore<TranscriptDiff> _transcriptStreams;
    private readonly ConcurrentDictionary<StreamId, StreamId> _translatingStreams = new();
    private readonly StreamStore<StringDiff> _translationStreams;

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Services.LogFor(GetType());
    [field: AllowNull, MaybeNull]
    private ILogger OpenAudioSegmentLog => field ??= Services.LogFor<OpenAudioSegment>();
    [field: AllowNull, MaybeNull]
    private ILogger AudioSourceLog  => field ??= Services.LogFor<AudioSource>();
    private static bool DebugMode => Constants.DebugMode.AudioProcessor;
    private ILogger? DebugLog => DebugMode ? Log : null;
    private IServiceProvider Services { get; }
    [field: AllowNull, MaybeNull]
    private MeshNode ThisNode => field ??= Services.MeshWatcher().ThisNode;
    [field: AllowNull, MaybeNull]
    private AudioSegmentSaver AudioSegmentSaver => field ??= Services.GetRequiredService<AudioSegmentSaver>();
    [field: AllowNull, MaybeNull]
    private ITranscriberFactory TranscriberFactory => field ??= Services.GetRequiredService<ITranscriberFactory>();
    [field: AllowNull, MaybeNull]
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    [field: AllowNull, MaybeNull]
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    [field: AllowNull, MaybeNull]
    private IServerKvas ServerKvas => field ??= Services.GetRequiredService<IServerKvas>();
    [field: AllowNull, MaybeNull]
    private ICommander Commander => field ??= Services.Commander();
    [field: AllowNull, MaybeNull]
    private MomentClockSet Clocks => field ??= Services.Clocks();
    [field: AllowNull, MaybeNull]
    private AudioSettings AudioSettings => field ??= Services.GetRequiredService<AudioSettings>();
    [field: AllowNull, MaybeNull]
    private IQueues Queues => field ??= Services.Queues();

    public StreamingBackend(IServiceProvider services)
    {
        Services = services;

        _audioStreams = new StreamStore<byte[]> {
            StreamIdValidator = ValidateStreamId,
            StreamCount = AppMeters.AudioStreamCount,
            ExpirationDelay = AudioSettings.StreamExpirationDelay,
            Log = services.LogFor($"{GetType().FullName}.AudioStreams"),
        };
        _transcriptStreams = new StreamStore<TranscriptDiff> {
            StreamIdValidator = ValidateStreamId,
            ExpirationDelay = AudioSettings.StreamExpirationDelay,
            Log = services.LogFor($"{GetType().FullName}.TranscriptStreams"),
            OnStreamExpire = id => _translatingStreams.Remove(id, out _),
        };
		_translationStreams = new StreamStore<StringDiff> {
            Log = services.LogFor($"{GetType().FullName}.TranslationStreams"),
        };
    }

    public void Dispose()
    {
        _audioStreams.Dispose();
        _transcriptStreams.Dispose();
    }

    public virtual async Task<RpcStream<byte[]>?> GetAudio(StreamId streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        var stream = await _audioStreams.Get(streamId, cancellationToken).ConfigureAwait(false);
        if (stream == null)
            return null;

        stream = SkipTo(stream, skipTo, cancellationToken);
        return RpcStream.New(stream);
    }

    public virtual async Task<RpcStream<TranscriptDiff>?> GetTranscript(StreamId streamId, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("GetTranscript: {StreamId}", streamId);
        var stream = await _transcriptStreams.Get(streamId, false, cancellationToken).ConfigureAwait(false);
        if (stream != null)
            return RpcStream.New(stream);

        var language = streamId.Language;
        if (language == null)
            return null;

        var originalStreamId = StreamId.New(streamId.NodeRef, streamId.LocalId);
        if (!_translatingStreams.TryAdd(streamId, originalStreamId)) {
            stream = await _transcriptStreams.Get(streamId, true, cancellationToken).ConfigureAwait(false);
            return RpcStream.New(stream!); // Already translating
        }

        DebugLog?.LogDebug("GetTranscript: {StreamId} - Translate stream", streamId);
        var cmd = new TranslationsBackend_TranslateStream(originalStreamId, language);
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        stream = await _transcriptStreams.Get(streamId, true, cancellationToken).ConfigureAwait(false);

        DebugLog?.LogDebug("GetTranscript: {StreamId} - Return stream", streamId);
        return stream == null
            ? null
            : RpcStream.New(stream);
    }

    public async Task PushTranscript(StreamId streamId, RpcStream<TranscriptDiff> diffStream, CancellationToken cancellationToken)
    {
        ValidateStreamId(streamId);
        if (diffStream is null)
            throw new ArgumentNullException(nameof(diffStream));

        await _transcriptStreams.Publish(streamId, diffStream).ConfigureAwait(false);
    }

    public async Task<RpcStream<StringDiff>?> GetTranslation(StreamId streamId, CancellationToken cancellationToken)
    {
        var stream = await _translationStreams.Get(streamId, cancellationToken).ConfigureAwait(false);
        return stream == null
            ? null
            : RpcStream.New(stream);
    }

    public Task PublishTranslation(
        StreamId streamId,
        IAsyncEnumerable<StringDiff> stream,
        CancellationToken cancellationToken)
        => _translationStreams.Publish(streamId, stream);

    // Private methods

    private void ValidateStreamId(StreamId streamId)
    {
        if (streamId.NodeRef != ThisNode.Ref)
            throw new ArgumentOutOfRangeException(nameof(streamId),
                $"Wrong mesh node: expected {ThisNode.Ref}, but got {streamId.NodeRef}.");
    }

    private static IAsyncEnumerable<byte[]> SkipTo(
        IAsyncEnumerable<byte[]> stream,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        // This method assumes there are 20ms packets!
        // And the first packet is the header
        if (skipTo <= TimeSpan.Zero)
            return stream;

        var skipToFrameN = (int)skipTo.TotalMilliseconds / 20;
        var (headerDataTask, dataStream) = stream.SplitHead(cancellationToken);
        return dataStream
            .SkipWhile((_, i) => i < skipToFrameN)
            .Prepend(headerDataTask);
    }
}
