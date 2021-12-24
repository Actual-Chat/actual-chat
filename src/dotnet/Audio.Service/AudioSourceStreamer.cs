using ActualChat.Audio.Db;
using ActualChat.Media;
using ActualChat.Redis;
using Stl.Redis;

namespace ActualChat.Audio;

public class AudioSourceStreamer : IAudioSourceStreamer
{
    private const int StreamBufferSize = 64;
    private const int MaxStreamDuration = 600;

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
        var (formatTask, frames) = audioStream.ToMediaFrames(cancellationToken);
        var audio = new AudioSource(formatTask, frames, audioLog, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    public IAsyncEnumerable<AudioStreamPart> GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        if (skipTo > TimeSpan.FromSeconds(MaxStreamDuration))
            return AsyncEnumerable.Empty<AudioStreamPart>();

        var streamer = RedisDb.GetStreamer<AudioStreamPart>(streamId);
        var audioStream = streamer
            .Read(cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);
        if (skipTo == TimeSpan.Zero)
            return audioStream;

        var audioLog = LoggerFactory.CreateLogger<AudioSource>();
        var (formatTask, frames) = audioStream.ToMediaFrames(cancellationToken);
        var audio = new AudioSource(formatTask, frames, audioLog, cancellationToken);
        return ToStream(formatTask, audio.SkipTo(skipTo, cancellationToken).GetFrames(cancellationToken), cancellationToken);
    }

    public Task Publish(string streamId, AudioSource audio, CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer(streamId, new RedisStreamer<AudioStreamPart>.Options {
            MaxStreamLength = 10 * 1024,
        });
        var audioStream = ToStream(audio.GetFormatTask(), audio.GetFrames(cancellationToken), cancellationToken);
        return streamer.Write(audioStream, cancellationToken);
    }

    public async IAsyncEnumerable<AudioStreamPart> ToStream(
        Task<AudioFormat> formatTask,
        IAsyncEnumerable<AudioFrame> frames,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var format = await formatTask.WithFakeCancellation(cancellationToken).ConfigureAwait(false);
        yield return new AudioStreamPart(format);
        await foreach (var frame in frames.ConfigureAwait(false))
            yield return new AudioStreamPart(frame);
    }
}
