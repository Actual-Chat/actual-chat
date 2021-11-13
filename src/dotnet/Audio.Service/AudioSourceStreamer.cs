using ActualChat.Audio.Db;
using ActualChat.Redis;
using Stl.Redis;

namespace ActualChat.Audio;

public class AudioSourceStreamer : IAudioSourceStreamer
{
    private const int StreamBufferSize = 64;
    private readonly RedisDb _redisDb;
    private readonly ILoggerFactory _loggerFactory;

    public AudioSourceStreamer(
        RedisDb<AudioContext> audioRedisDb,
        ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _redisDb = audioRedisDb.WithKeyPrefix("audio-sources");
    }

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
        var audioStream = streamer.Read(cancellationToken).Buffer(StreamBufferSize, cancellationToken);
        var audioLog = _loggerFactory.CreateLogger<AudioSource>();
        var audio = new AudioSource(audioStream, audioLog, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    public IAsyncEnumerable<AudioStreamPart> GetAudioStream(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<AudioStreamPart>(streamId);
        return streamer.Read(cancellationToken).Buffer(StreamBufferSize, cancellationToken);
    }
}
