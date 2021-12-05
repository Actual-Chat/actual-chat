using ActualChat.Audio.Db;
using ActualChat.Redis;
using ActualChat.Transcription;
using Stl.Redis;

namespace ActualChat.Audio;

public class TranscriptStreamer : ITranscriptStreamer
{
    private const int StreamBufferSize = 64;
    // ReSharper disable once UnusedAutoPropertyAccessor.Local
    private ILogger<TranscriptStreamer> Log { get; }
    private RedisDb RedisDb { get; }

    public TranscriptStreamer(
        RedisDb<AudioContext> audioRedisDb,
        ILogger<TranscriptStreamer> log)
    {
        Log = log;
        RedisDb = audioRedisDb.WithKeyPrefix("transcripts");
    }

    public Task Publish(
        StreamId streamId,
        IAsyncEnumerable<TranscriptUpdate> transcriptStream,
        CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer<TranscriptUpdate>(streamId);
        return streamer.Write(transcriptStream, cancellationToken);
    }

    public IAsyncEnumerable<TranscriptUpdate> GetTranscriptStream(
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer<TranscriptUpdate>(streamId);
        return streamer.Read(cancellationToken).Buffer(StreamBufferSize, cancellationToken);
    }
}
