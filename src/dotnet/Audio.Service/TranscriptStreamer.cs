using ActualChat.Redis;

namespace ActualChat.Audio;

public class TranscriptStreamer : ITranscriptStreamer
{
    private readonly ILogger<TranscriptStreamer> _log;
    private readonly RedisDb _redisDb;

    public TranscriptStreamer(
        RedisDb rootRedisDb,
        ILogger<TranscriptStreamer> log)
    {
        _log = log;
        _redisDb = rootRedisDb.WithKeyPrefix("transcripts");
    }

    public Task PublishTranscriptStream(StreamId streamId, ChannelReader<TranscriptPart> channel, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<TranscriptPart>(streamId);
        return streamer.Write(channel, cancellationToken);
    }

    public Task<ChannelReader<TranscriptPart>> GetTranscriptStream(StreamId streamId, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<TranscriptPart>(streamId);
        return Task.FromResult(streamer.Read(cancellationToken));
    }
}
