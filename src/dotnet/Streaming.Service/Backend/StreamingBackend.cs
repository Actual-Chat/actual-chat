using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Diagnostics;
using ActualChat.Kvas;
using ActualChat.Mesh;
using ActualChat.Streaming.Services;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public partial class StreamingBackend : IStreamingBackend, IDisposable
{
    public record Options
    {
        public TimeSpan TranscriptDebouncePeriod { get; set; } = TimeSpan.FromSeconds(0.2);
        public TimeSpan CancellationDelay { get; set; } = TimeSpan.FromSeconds(3);
        public bool IsEnabled { get; init; } = true;
    }

    private readonly StreamStore<byte[]> _audioStreams;
    private readonly StreamStore<TranscriptDiff> _transcriptStreams;

    private ILogger Log { get; }
    private ILogger OpenAudioSegmentLog { get; }
    private ILogger AudioSourceLog { get; }
    private static bool DebugMode => Constants.DebugMode.AudioProcessor;
    private ILogger? DebugLog => DebugMode ? Log : null;

    private Options Settings { get; }
    private IServiceProvider Services { get; }
    private MeshNode OwnNode { get; }
    private AudioSegmentSaver AudioSegmentSaver { get; }
    private ITranscriberFactory TranscriberFactory { get; }
    private IChats Chats { get; }
    private IAuthors Authors { get; }
    private IServerKvas ServerKvas { get; }
    private ICommander Commander { get; }
    private MomentClockSet Clocks { get; }

    public StreamingBackend(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Log = services.LogFor(GetType());
        OpenAudioSegmentLog = services.LogFor<OpenAudioSegment>();
        AudioSourceLog = services.LogFor<AudioSource>();

        OwnNode = services.MeshWatcher().OwnNode;
        AudioSegmentSaver = services.GetRequiredService<AudioSegmentSaver>();
        TranscriberFactory = services.GetRequiredService<ITranscriberFactory>();
        Chats = services.GetRequiredService<IChats>();
        Authors = services.GetRequiredService<IAuthors>();
        ServerKvas = services.ServerKvas();
        Commander = services.Commander();
        Clocks = services.Clocks();

        _audioStreams = new StreamStore<byte[]>() {
            StreamIdValidator = ValidateStreamId,
            StreamCount = AppMeters.AudioStreamCount,
            Log = services.LogFor($"{GetType().FullName}.AudioStreams"),
        };
        _transcriptStreams = new StreamStore<TranscriptDiff>() {
            StreamIdValidator = ValidateStreamId,
            Log = services.LogFor($"{GetType().FullName}.TranscriptStreams"),
        };
    }

    public void Dispose()
    {
        _audioStreams.Dispose();
        _transcriptStreams.Dispose();
    }

    public async Task<RpcStream<byte[]>?> GetAudio(StreamId streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        var stream = await _audioStreams.Get(streamId, cancellationToken).ConfigureAwait(false);
        if (stream == null)
            return null;

        stream = SkipTo(stream, skipTo, cancellationToken);
        return RpcStream.New(stream);
    }

    public async Task<RpcStream<TranscriptDiff>?> GetTranscript(StreamId streamId, CancellationToken cancellationToken)
    {
        var stream = await _transcriptStreams.Get(streamId, cancellationToken).ConfigureAwait(false);
        return stream == null ? null
            : RpcStream.New(stream);
    }

    // Private methods

    private void ValidateStreamId(StreamId streamId)
    {
        if (streamId.NodeRef != OwnNode.Ref)
            throw new ArgumentOutOfRangeException(nameof(streamId),
                $"Wrong mesh node: expected {OwnNode.Ref}, but got {streamId.NodeRef}.");
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
