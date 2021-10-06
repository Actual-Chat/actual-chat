using ActualChat.Channels;
using ActualChat.Redis;

namespace ActualChat.Audio;

public class AudioSourceStreamer : IAudioSourceStreamer
{
    private readonly ILogger<AudioStreamer> _log;
    private readonly RedisDb _redisDb;

    public AudioSourceStreamer(
        RedisDb rootRedisDb,
        ILogger<AudioStreamer> log)
    {
        _log = log;
        _redisDb = rootRedisDb.WithKeyPrefix("audio");
    }

    public Task PublishAudioSource(StreamId streamId, AudioSource audioSource, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<AudioSourcePart>(streamId);
        var channel = Channel.CreateUnbounded<AudioSourcePart>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        _ = Task.Run(() => TransformFramesForStreaming(audioSource, channel, cancellationToken), cancellationToken);

        return streamer.Write(channel, cancellationToken);
    }

    public Task<ChannelReader<AudioSourcePart>> GetAudioSourceParts(StreamId streamId, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<AudioSourcePart>(streamId);
        return Task.FromResult(streamer.Read(cancellationToken));
    }

    public async Task<AudioSource> GetAudioSource(StreamId streamId, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<AudioSourcePart>(streamId);
        var audioSourcePartReader = streamer.Read(cancellationToken);
        return await AudioSourceHelper.ConvertToAudioSource(audioSourcePartReader, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask TransformFramesForStreaming(
        AudioSource audioSource,
        ChannelWriter<AudioSourcePart> writer,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            writer.TryWrite(new AudioSourcePart(audioSource.Format, Frame: null, Duration: null));
            await foreach (var audioFrame in audioSource.Frames.WithCancellation(cancellationToken))
                writer.TryWrite(new AudioSourcePart(Format:null, Frame: audioFrame, Duration: null));
            var duration = await audioSource.Duration.ConfigureAwait(false);
            writer.TryWrite(new AudioSourcePart(Format:null, Frame: null, Duration: duration));
        }
        catch (Exception e) {
            error = e;
        }
        finally {
            writer.TryComplete(error);
        }
    }

}
