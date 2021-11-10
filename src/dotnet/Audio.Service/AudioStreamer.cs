using ActualChat.Audio.Db;
using ActualChat.Blobs;
using ActualChat.Redis;
using Stl.Redis;

namespace ActualChat.Audio;

public class AudioStreamer : IAudioStreamer
{
    private readonly ILogger<AudioStreamer> _log;
    private readonly RedisDb _redisDb;

    public AudioStreamer(
        RedisDb<AudioContext> audioRedisDb,
        ILogger<AudioStreamer> log)
    {
        _log = log;
        _redisDb = audioRedisDb.WithKeyPrefix("audio-streams");
    }

    public Task PublishAudioStream(StreamId streamId, IAsyncEnumerable<BlobPart> blobParts, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<BlobPart>(streamId);
        return streamer.Write(blobParts, cancellationToken);
    }

    public IAsyncEnumerable<BlobPart> GetAudioBlobStream(StreamId streamId, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<BlobPart>(streamId);
        return streamer.Read(cancellationToken);
    }
}
