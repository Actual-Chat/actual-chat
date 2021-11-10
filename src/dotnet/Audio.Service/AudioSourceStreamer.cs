using ActualChat.Audio.Db;
using ActualChat.Redis;
using Stl.Redis;

namespace ActualChat.Audio;

public class AudioSourceStreamer : IAudioSourceStreamer
{
    private readonly RedisDb _redisDb;

    public AudioSourceStreamer(RedisDb<AudioContext> audioRedisDb)
        => _redisDb = audioRedisDb.WithKeyPrefix("audio-sources");

    public Task Publish(StreamId streamId, AudioSource audio, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<AudioStreamPart>(streamId);
        var audioStream = audio.GetStream(cancellationToken);
        return streamer.Write(audioStream, cancellationToken);
    }

    public async Task<AudioSource> GetAudio(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<AudioStreamPart>(streamId);
        var audioStream = streamer.Read(cancellationToken);
        var audio = new AudioSource(audioStream, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    public IAsyncEnumerable<AudioStreamPart> GetAudioStream(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<AudioStreamPart>(streamId);
        return streamer.Read(cancellationToken);
    }
}
