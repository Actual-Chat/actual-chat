using ActualChat.Audio.Db;
using ActualChat.Redis;
using Stl.Redis;

namespace ActualChat.Audio;

public class AudioSourceStreamer : IAudioSourceStreamer
{
    private const int StreamBufferSize = 64;

    private ILoggerFactory LoggerFactory { get; }
    private RedisDb RedisDb { get; }

    public AudioSourceStreamer(
        RedisDb<AudioContext> audioRedisDb,
        ILoggerFactory loggerFactory)
    {
        LoggerFactory = loggerFactory;
        RedisDb = audioRedisDb.WithKeyPrefix("audio-sources");
    }

    public async Task<AudioSource> GetAudio(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var audioStream = GetAudioStream(streamId, skipTo, cancellationToken);
        var audioLog = LoggerFactory.CreateLogger<AudioSource>();
        var audio = new AudioSource(audioStream, audioLog, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    public IAsyncEnumerable<AudioStreamPart> GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer<AudioStreamPart>(streamId);
        var audioStream = streamer.Read(cancellationToken).Buffer(StreamBufferSize, cancellationToken);
        if (skipTo == TimeSpan.Zero)
            return audioStream;

        var audioLog = LoggerFactory.CreateLogger<AudioSource>();
        var audio = new AudioSource(audioStream, audioLog, cancellationToken);
        return audio.SkipTo(skipTo, cancellationToken).GetStream(cancellationToken);
    }

    public Task Publish(string streamId, AudioSource audio, CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer<AudioStreamPart>(streamId);
        var audioStream = audio.GetStream(cancellationToken);
        return streamer.Write(audioStream, cancellationToken);
    }
}
