using ActualChat.Audio.Db;
using ActualChat.Blobs;
using ActualChat.Redis;
using Stl.Redis;

namespace ActualChat.Audio;

public class AudioSourceStreamer : IAudioSourceStreamer
{
    private readonly RedisDb _redisDb;

    public AudioSourceStreamer(RedisDb<AudioContext> audioRedisDb)
        => _redisDb = audioRedisDb.WithKeyPrefix("audio-sources");

    public async Task<AudioSource> GetAudioSource(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<AudioSourcePart>(streamId);
        var parts = streamer.Read(cancellationToken);
        if (skipTo == default)
            return await parts.ToAudioSource(cancellationToken).ConfigureAwait(false);

        var channel = Channel.CreateUnbounded<AudioSourcePart>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        _ = Task.Run(() => ApplySkipTo(parts, channel, skipTo, cancellationToken), cancellationToken);

        return await channel.Reader.ToAudioSource(cancellationToken).ConfigureAwait(false);
    }

    public Task<ChannelReader<AudioSourcePart>> GetAudioSourceParts(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<AudioSourcePart>(streamId);
        var parts = streamer.Read(cancellationToken);
        if (skipTo == default)
            return Task.FromResult(parts);

        var channel = Channel.CreateUnbounded<AudioSourcePart>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        _ = Task.Run(() => ApplySkipTo(parts, channel, skipTo, cancellationToken), cancellationToken);

        return Task.FromResult(channel.Reader);
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

    // Private methods

    private static async Task ApplySkipTo(
        ChannelReader<AudioSourcePart> reader,
        ChannelWriter<AudioSourcePart> writer,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            var channel = Channel.CreateUnbounded<BlobPart>(new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            });

            _ = Task.Run(() => TransformSourcePartToBlobPart(reader, channel, cancellationToken), cancellationToken);

            var audioSourceProvider = new AudioSourceProvider();
            var audioSourceWithOffset = await audioSourceProvider
                .CreateMediaSource(
                    channel.Reader.ReadAllAsync(cancellationToken), skipTo,
                    cancellationToken)
                .ConfigureAwait(false);

            await TransformFramesForStreaming(audioSourceWithOffset, writer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
        }
        finally {
            writer.TryComplete(error);
        }
    }

    private static async Task TransformSourcePartToBlobPart(
        ChannelReader<AudioSourcePart> reader,
        ChannelWriter<BlobPart> writer,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        byte[]? header = null;
        try {
            var index = 0;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (reader.TryRead(out var sourcePart))
                if (sourcePart.Format != null) {
                    var format = sourcePart.Format;
                    header = Convert.FromBase64String(format.CodecSettings);
                }
                else if (sourcePart.Frame != null) {
                    var chunk = sourcePart.Frame.Data;
                    if (header != null) {
                        if (index != 0)
                            throw new InvalidOperationException("Format source part should be the first.");

                        var buffer = new byte[header.Length + chunk.Length];
                        header.CopyTo(buffer, 0);
                        chunk.CopyTo(buffer, header.Length);
                        await writer.WriteAsync(new BlobPart(index++, buffer), cancellationToken).ConfigureAwait(false);
                        header = null;
                    }
                    else
                        await writer.WriteAsync(new BlobPart(index++, chunk), cancellationToken).ConfigureAwait(false);
                }
        }
        catch (Exception e) {
            error = e;
        }
        finally {
            writer.TryComplete(error);
        }
    }

    private static async Task TransformFramesForStreaming(
        AudioSource audioSource,
        ChannelWriter<AudioSourcePart> writer,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            writer.TryWrite(new AudioSourcePart(audioSource.Format, null, null));
            await foreach (var audioFrame in audioSource.Frames.WithCancellation(cancellationToken))
                writer.TryWrite(new AudioSourcePart(null, audioFrame, null));
            var duration = await audioSource.DurationTask.ConfigureAwait(false);
            writer.TryWrite(new AudioSourcePart(null, null, duration));
        }
        catch (Exception e) {
            error = e;
        }
        finally {
            writer.TryComplete(error);
        }
    }
}
