using ActualChat.Audio.Db;
using ActualChat.Audio.Processing;
using ActualChat.Media;
using Stl.Redis;

namespace ActualChat.Audio;

public class AudioSourceStreamer : AudioProcessorBase, IAudioSourceStreamer
{
    private const int StreamBufferSize = 64;
    private const int MaxStreamDuration = 600;

    private ILogger AudioSourceLog { get; }
    private RedisDb RedisDb { get; }

    public AudioSourceStreamer(IServiceProvider services) : base(services)
    {
        AudioSourceLog = Services.LogFor<AudioSource>();
        var audioRedisDb = Services.GetRequiredService<RedisDb<AudioContext>>();
        RedisDb = audioRedisDb.WithKeyPrefix("audio-sources");
    }

    public async Task<AudioSource> GetAudio(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        // SkipTo via AudioSource is more efficient than SkipTo via GetAudioStream
        var audioStream = GetAudioStream(streamId, TimeSpan.Zero, cancellationToken);
        var (formatTask, frames) = audioStream.ToMediaFrames(cancellationToken);
        var audio = new AudioSource(formatTask, frames, AudioSourceLog, cancellationToken);
        if (skipTo >= TimeSpan.Zero)
            audio = audio.SkipTo(skipTo, cancellationToken);
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

        var streamer = RedisDb.GetStreamer<AudioStreamPart>(streamId, new() {
            AppendCheckPeriod = TimeSpan.FromMilliseconds(250),
        });
        var audioStream = streamer
            .Read(cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);
        return SkipTo(audioStream, skipTo, cancellationToken);
    }

    public Task Publish(string streamId, AudioSource audio, CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer<AudioStreamPart>(streamId, new() {
            MaxStreamLength = 10 * 1024,
        });
        var audioStream = ToAudioStream(audio.GetFormatTask(), audio.GetFrames(cancellationToken), cancellationToken);
        return streamer.Write(audioStream, cancellationToken);
    }

    private IAsyncEnumerable<AudioStreamPart> SkipTo(
        IAsyncEnumerable<AudioStreamPart> audioStream,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        if (skipTo <= TimeSpan.Zero) return audioStream;

        var (formatTask, frames) = audioStream.ToMediaFrames(cancellationToken);
        var audio = new AudioSource(formatTask, frames, AudioSourceLog, cancellationToken);
        return ToAudioStream(formatTask,
            audio.SkipTo(skipTo, cancellationToken).GetFrames(cancellationToken),
            cancellationToken);
    }

    private async IAsyncEnumerable<AudioStreamPart> ToAudioStream(
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
