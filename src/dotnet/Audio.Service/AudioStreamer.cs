using ActualChat.Audio.Db;
using ActualChat.Audio.Processing;
using Stl.Redis;

namespace ActualChat.Audio;

public class AudioStreamer : AudioProcessorBase, IAudioStreamer
{
    private readonly AudioSettings _settings;
    private const int StreamBufferSize = 64;
    private const int MaxStreamDuration = 600;

    private ILogger AudioSourceLog { get; }
    private RedisDb RedisDb { get; }

    public AudioStreamer(IServiceProvider services) : base(services)
    {
        AudioSourceLog = Services.LogFor<AudioSource>();
        var audioRedisDb = Services.GetRequiredService<RedisDb<AudioContext>>();
        RedisDb = audioRedisDb.WithKeyPrefix("audio-sources");
        _settings = Services.GetRequiredService<AudioSettings>();
    }

    public Task<AudioSource> GetAudio(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var audioStream = GetAudioStream(streamId, skipTo, cancellationToken);
        var frameStream = audioStream
            .Select((packet, i) => new AudioFrame {
                Data = packet,
                Offset = TimeSpan.FromMilliseconds(i * 20), // we support only 20-ms packets
            });
        return Task.FromResult(new AudioSource(
            Task.FromResult(AudioSource.DefaultFormat),
            frameStream,
            TimeSpan.Zero,
            AudioSourceLog,
            cancellationToken));
    }

    public IAsyncEnumerable<byte[]> GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        if (skipTo > TimeSpan.FromSeconds(MaxStreamDuration))
            return AsyncEnumerable.Empty<byte[]>();

        var streamer = RedisDb.GetStreamer<byte[]>(streamId, new() {
            AppendCheckPeriod = TimeSpan.FromMilliseconds(250),
        });
        var audioStream = streamer
            .Read(cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);
        return SkipTo(audioStream, skipTo);
    }

    public Task Publish(string streamId, AudioSource audio, CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer<byte[]>(streamId, new() {
            MaxStreamLength = 10 * 1024,
            ExpirationPeriod = _settings.StreamExpirationPeriod,
        });
        var audioStream = audio
            .GetFrames(cancellationToken)
            .Select(f => f.Data);
        return streamer.Write(audioStream, cancellationToken);
    }

    /// <summary>
    /// Expects 20ms packets
    /// </summary>
    /// <param name="audioStream">stream of 20ms long Opus packets</param>
    /// <param name="skipTo"></param>
    /// <returns>Stream without skipped packets</returns>
    private IAsyncEnumerable<byte[]> SkipTo(
        IAsyncEnumerable<byte[]> audioStream,
        TimeSpan skipTo)
    {
        if (skipTo <= TimeSpan.Zero)
            return audioStream;

        var skipToFrameN = (int)skipTo.TotalMilliseconds / 20;
        return audioStream
            .SkipWhile((_, i) => i < skipToFrameN);
    }
}
