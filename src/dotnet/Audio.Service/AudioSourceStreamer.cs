using ActualChat.Audio.Db;
using ActualChat.Redis;
using Stl.Redis;

namespace ActualChat.Audio;

public class AudioSourceStreamer : IAudioSourceStreamer
{
    private const int StreamBufferSize = 64;

    private readonly ILoggerFactory _loggerFactory;
    private readonly RedisDb _redisDb;

    public AudioSourceStreamer(
        RedisDb<AudioContext> audioRedisDb,
        ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _redisDb = audioRedisDb.WithKeyPrefix("audio-sources");
    }

    public async Task<AudioSource> GetAudio(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var audioStream = GetAudioStream(streamId, skipTo, cancellationToken);
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
        var audioStream = streamer.Read(cancellationToken).Buffer(StreamBufferSize, cancellationToken);
        if (skipTo == TimeSpan.Zero)
            return audioStream;

        var audioLog = _loggerFactory.CreateLogger<AudioSource>();
        var audio = new AudioSource(audioStream, audioLog, cancellationToken);
        return audio.SkipTo(skipTo, cancellationToken).GetStream(cancellationToken);
    }

    public Task Publish(StreamId streamId, AudioSource audio, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<AudioStreamPart>(streamId);
        var audioStream = audio.GetStream(cancellationToken);
        return streamer.Write(audioStream, cancellationToken);
    }
}
