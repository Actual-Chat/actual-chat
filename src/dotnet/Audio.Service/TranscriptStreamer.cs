using ActualChat.Audio.Db;
using ActualChat.Redis;
using ActualChat.Transcription;
using Stl.Redis;

namespace ActualChat.Audio;

public class TranscriptStreamer : ITranscriptStreamer
{
    private const int StreamBufferSize = 64;
    private readonly ILogger<TranscriptStreamer> _log;
    private readonly RedisDb _redisDb;

    public TranscriptStreamer(
        RedisDb<AudioContext> audioRedisDb,
        ILogger<TranscriptStreamer> log)
    {
        _log = log;
        _redisDb = audioRedisDb.WithKeyPrefix("transcripts");
    }

    public Task Publish(
        StreamId streamId,
        IAsyncEnumerable<TranscriptUpdate> transcriptStream,
        CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<TranscriptUpdate>(streamId);
        return streamer.Write(transcriptStream, cancellationToken);
    }

    public IAsyncEnumerable<TranscriptUpdate> GetTranscriptStream(
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<TranscriptUpdate>(streamId);
        return streamer.Read(cancellationToken).Buffer(64, cancellationToken);
    }
}
