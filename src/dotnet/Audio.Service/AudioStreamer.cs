using ActualChat.Blobs;
using ActualChat.Redis;

namespace ActualChat.Audio;

public class AudioStreamer : IAudioStreamer
{
    private readonly ILogger<AudioStreamer> _log;
    private readonly RedisDb _redisDb;

    public AudioStreamer(
        RedisDb rootRedisDb,
        ILogger<AudioStreamer> log)
    {
        _log = log;
        _redisDb = rootRedisDb.WithKeyPrefix("audio-records");
    }

    public Task PublishAudioStream(StreamId streamId, ChannelReader<BlobPart> channel, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<BlobPart>(streamId);
        return streamer.Write(channel, cancellationToken);
    }

    public Task<ChannelReader<BlobPart>> GetAudioStream(StreamId streamId, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<BlobPart>(streamId);
        return Task.FromResult(streamer.Read(cancellationToken));
    }
}
