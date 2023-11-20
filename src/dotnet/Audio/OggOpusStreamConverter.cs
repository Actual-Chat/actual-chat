using System.Buffers;
using ActualChat.Audio.Ogg;
using ActualChat.Spans;

namespace ActualChat.Audio;

public class OggOpusStreamConverter(OggOpusStreamConverter.Options? options = null) : IAudioStreamConverter
{
    public record Options
    {
        public uint StreamSerialNumber { get; init; } = 0;
        public TimeSpan PageDuration { get; init; } = TimeSpan.FromMilliseconds(200);
    }

    public Task<AudioSource> FromByteStream(IAsyncEnumerable<byte[]> byteStream, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public async IAsyncEnumerable<(byte[] Buffer, AudioFrame? LastFrame)> ToByteFrameStream(
        AudioSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var bufferLease = MemoryPool<byte>.Shared.Rent(8 * 1024);
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
                PreSkip = (ushort)source.Format.PreSkipFrames,
                InputSampleRate = 48000,
                OutputGain = 0,
                ChannelMapping = 0,
            });
        position += WriteTags(new OggOpusWriter(state, buffer.Span[position..]), new OpusTags());
        var pageDuration = options?.PageDuration.TotalMilliseconds ?? 1000;
        var frameChunks = source
            .GetFrames(cancellationToken)
            .WithHasNext(cancellationToken)
            .ChunkWhile(list => list.Sum(t => t.Duration.Milliseconds) <= pageDuration, cancellationToken);
        await foreach (var (audioFrames, hasNext) in frameChunks) {
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
