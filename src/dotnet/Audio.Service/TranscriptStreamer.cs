using ActualChat.Audio.Db;
using ActualChat.Transcription;
using Stl.Redis;

namespace ActualChat.Audio;

public class TranscriptStreamer : ITranscriptStreamer
{
    private ITranscriptStreamServer TranscriptStreamServer { get; }
    private ILogger<TranscriptStreamer> Log { get; }

    public TranscriptStreamer(
        ITranscriptStreamServer transcriptStreamServer,
        ILogger<TranscriptStreamer> log)
    {
        TranscriptStreamServer = transcriptStreamServer;
        Log = log;
    }

    public IAsyncEnumerable<Transcript> GetTranscriptDiffStream(
        Symbol streamId,
        CancellationToken cancellationToken)
    {
        var transcriptStream = TranscriptStreamServer.Read(streamId, cancellationToken);
        if (transcriptStream == AsyncEnumerable.Empty<Transcript>())
            Log.LogWarning("{TranscriptStreamServer} returns null transcript stream", TranscriptStreamServer.GetType().Name);
        return transcriptStream;
    }
}
