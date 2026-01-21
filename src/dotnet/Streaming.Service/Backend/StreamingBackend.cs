using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Diagnostics;
using ActualChat.Kvas;
using ActualChat.Streaming.Services;
using ActualChat.Transcription;
using ActualLab.Rpc;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Streaming;

public partial class StreamingBackend : IStreamingBackend, IDisposable
{
    private readonly StreamStore<byte[]> _audioStreams;
    private readonly StreamStore<TranscriptDiff> _transcriptStreams;
    private readonly ConcurrentDictionary<StreamId, StreamId> _translatingStreams = new();

    private ILogger Log => field ??= Services.LogFor(GetType());
    private ILogger OpenAudioSegmentLog => field ??= Services.LogFor<OpenAudioSegment>();
    private ILogger AudioSourceLog  => field ??= Services.LogFor<AudioSource>();
    private static bool DebugMode => Constants.DebugMode.AudioProcessor;
    private ILogger? DebugLog => DebugMode ? Log : null;

    private IServiceProvider Services { get; }
    private AudioSettings AudioSettings { get; }
    private MeshNode ThisNode => field ??= Services.MeshWatcher().ThisNode;
    private AudioSegmentSaver AudioSegmentSaver => field ??= Services.GetRequiredService<AudioSegmentSaver>();
    private ITranscriberFactory TranscriberFactory => field ??= Services.GetRequiredService<ITranscriberFactory>();
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private IServerKvas ServerKvas => field ??= Services.GetRequiredService<IServerKvas>();
    private ICommander Commander => field ??= Services.Commander();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private IHostApplicationLifetime HostLifetime => field ??= Services.HostLifetime();

    public StreamingBackend(IServiceProvider services)
    {
        Services = services;
        AudioSettings = services.GetRequiredService<AudioSettings>();

        var typeFullName = GetType().FullName;
        _audioStreams = new StreamStore<byte[]> {
            StreamIdValidator = ValidateStreamId,
            StreamCount = AppMeters.AudioStreamCount,
            ExpirationDelay = AudioSettings.StreamExpirationDelay,
            Log = services.LogFor($"{typeFullName}.AudioStreams"),
        };
        _transcriptStreams = new StreamStore<TranscriptDiff> {
            StreamIdValidator = ValidateStreamId,
            ExpirationDelay = AudioSettings.StreamExpirationDelay,
            OnStreamExpire = id => _translatingStreams.Remove(id, out _),
            Log = services.LogFor($"{typeFullName}.TranscriptStreams"),
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
        // Use ApplicationStopping as GetTranscript might be canceled, but we still want to wait for the translated stream to be created.
        await Commander.Call(cmd, HostLifetime.StopToken()).ConfigureAwait(false);
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
            .PrependOne(headerDataTask);
    }
}
