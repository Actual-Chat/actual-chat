using ActualChat.Audio.Ogg;

namespace ActualChat.Audio;

/// <summary>
/// Converts between the Ogg/Opus container and <see cref="AudioSource"/>.
/// </summary>
public sealed class OggOpusStreamConverter(
    OggOpusStreamConverter.Options? options = null,
    MomentClockSet? clocks = null,
    ILogger? log = null
    ) : IAudioStreamConverter
{
    public record Options
    {
        public uint StreamSerialNumber { get; init; } = 0;
        public TimeSpan PageDuration { get; init; } = TimeSpan.FromMilliseconds(200);
    }

    private MomentClockSet Clocks { get; } = clocks ?? MomentClockSet.Default;
    private ILogger Log { get; } = log ?? NullLogger.Instance;

    public async Task<AudioSource> FromByteStream(
        IAsyncEnumerable<byte[]> byteStream,
        CancellationToken cancellationToken = default)
    {
        var headerSource = AsyncTaskMethodBuilderExt.New<OpusHead>();
        var headerTask = headerSource.Task;
        var target = Channel.CreateBounded<AudioFrame>(
            new BoundedChannelOptions(Constants.Queues.OpusStreamConverterQueueSize) {
                SingleWriter = true,
                SingleReader = true,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        _ = BackgroundTask.Run(async () => {
            try {
                var reader = new OggOpusReader();
                await foreach (var frame in reader.ReadFrames(byteStream, cancellationToken).ConfigureAwait(false)) {
                    if (!headerTask.IsCompleted)
                        headerSource.SetResult(RequireMono(reader.Head!.Value));
                    await target.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                }
                if (!headerTask.IsCompleted && reader.Head is { } head)
                    headerSource.SetResult(RequireMono(head));
            }
            catch (OperationCanceledException e) {
                target.Writer.TryComplete(e);
                if (cancellationToken.IsCancellationRequested)
                    headerSource.TrySetCanceled(cancellationToken);
                else
                    headerSource.TrySetCanceled();
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "Ogg/Opus stream parse failed");
                target.Writer.TryComplete(e);
                headerSource.TrySetException(e);
                throw;
            }
            finally {
                target.Writer.TryComplete();
                if (!headerTask.IsCompleted)
                    headerSource.TrySetException(new InvalidOperationException("Format wasn't parsed."));
            }
        }, CancellationToken.None);

        var opusHead = await headerTask.ConfigureAwait(false);
        return new AudioSource(
            Clocks.SystemClock.Now,
            AudioSource.DefaultFormat with { PreSkip = opusHead.PreSkip },
            target.Reader.ReadAllAsync(cancellationToken),
            TimeSpan.Zero,
            Log,
            cancellationToken);

        static OpusHead RequireMono(OpusHead head)
            => head.OutputChannelCount == Constants.Audio.Channels
                ? head
                : throw StandardError.NotSupported($"Ogg/Opus stream has {head.OutputChannelCount} channels.");
    }

    public async IAsyncEnumerable<(byte[] Buffer, AudioFrame? LastFrame)> ToByteFrameStream(
        AudioSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var bufferLease = ArrayPools.SharedBytePool.Lease(8 * 1024);
        var buffer = bufferLease.Memory;
        var state = new OggOpusWriter.State {
            SerialNumber = options?.StreamSerialNumber == 0
                ? (uint)Random.Shared.Next()
                : options?.StreamSerialNumber ?? (uint)Random.Shared.Next(),
        };

        var position = WriteHead(new OggOpusWriter(state, buffer.Span),
            new OpusHead {
                Version = 1,
                OutputChannelCount = 1,
                PreSkip = (ushort)source.Format.PreSkip,
                InputSampleRate = 48000,
                OutputGain = 0,
                ChannelMapping = 0,
            });
        position += WriteTags(new OggOpusWriter(state, buffer.Span[position..]), new OpusTags());
        var pageDuration = options?.PageDuration.TotalMilliseconds ?? 1000;
        var frameChunks = source
            .GetFrames(cancellationToken)
            .ToMaybeHasNextSequence(cancellationToken)
            .ChunkWhile(list => list.Sum(t => t.Duration.Milliseconds) <= pageDuration, cancellationToken);
        await foreach (var (audioFrames, hasNext) in frameChunks.ConfigureAwait(false)) {
            position += WriteFrame(new OggOpusWriter(state, buffer.Span[position..]), audioFrames, hasNext);
            yield return (buffer.Span[..position].ToArray(), audioFrames[^1]);

            position = 0;
        }

        yield break;

        int WriteHead(OggOpusWriter writer, OpusHead opusHead)
        {
            if (!writer.Write(opusHead))
                throw new InvalidOperationException("Error writing Ogg stream. Buffer is too small.");

            return writer.Position;
        }

        int WriteTags(OggOpusWriter writer, OpusTags opusTags)
        {
            if (!writer.Write(opusTags))
                throw new InvalidOperationException("Error writing Ogg stream. Buffer is too small.");

            return writer.Position;
        }

        int WriteFrame(OggOpusWriter writer, IReadOnlyCollection<AudioFrame> audioFrames, bool hasNext)
        {
            if (!writer.Write(audioFrames, hasNext))
                throw new InvalidOperationException("Error writing Ogg stream. Buffer is too small.");

            return writer.Position;
        }
    }
}
