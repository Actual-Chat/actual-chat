using ActualChat.Audio.Db;
using ActualChat.Redis;
using Stl.Redis;

namespace ActualChat.Audio;

public class AudioStreamer : IAudioStreamer
{
    private const int StreamBufferSize = 64;
    // ReSharper disable once UnusedAutoPropertyAccessor.Local
    private ILogger<AudioStreamer> Log { get; }
    private RedisDb RedisDb { get; }

    public AudioStreamer(
        RedisDb<AudioContext> audioRedisDb,
        ILogger<AudioStreamer> log)
    {
        Log = log;
        RedisDb = audioRedisDb.WithKeyPrefix("audio-streams");
    }

    public Task Publish(string streamId, IAsyncEnumerable<BlobPart> blobParts, CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer<BlobPart>(streamId);
        return streamer.Write(blobParts, cancellationToken);
    }

    public IAsyncEnumerable<BlobPart> GetAudioBlobStream(string streamId, CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer<BlobPart>(streamId);
        return streamer.Read(cancellationToken).Buffer(StreamBufferSize, cancellationToken);
    }
}
