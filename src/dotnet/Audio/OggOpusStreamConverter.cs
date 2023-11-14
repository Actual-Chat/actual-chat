using System.Buffers;
using ActualChat.Audio.Ogg;
using ActualChat.Spans;

namespace ActualChat.Audio;

public class OggOpusStreamConverter : IAudioStreamConverter
{
    public Task<AudioSource> FromByteStream(IAsyncEnumerable<byte[]> byteStream, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public async IAsyncEnumerable<(byte[] Buffer, AudioFrame? LastFrame)> ToByteFrameStream(
        AudioSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var bufferLease = MemoryPool<byte>.Shared.Rent(4 * 1024);
        var buffer = bufferLease.Memory;
        var position = 0;
        var state = new OggOpusWriter.State();

        position = WriteHead(new OggOpusWriter(state, buffer.Span),
            new OpusHead {
                Version = 1,
                OutputChannelCount = 1,
                PreSkip = (ushort)source.Format.PreSkipFrames,
                InputSampleRate = 48000,
                OutputGain = 0,
                ChannelMapping = 0,
            });
        position = WriteTags(new OggOpusWriter(state, buffer.Span[position..]),
            new OpusTags());
        // chunks 100ms = 20ms * 5
        await foreach (var audioFrames in source.GetFrames(cancellationToken).Chunk(5, cancellationToken)) {
            position = WriteFrame(new OggOpusWriter(state, buffer.Span[position..]), audioFrames);
            yield return (buffer.Span[..position].ToArray(), audioFrames[^1]);

            position = 0;
        }

        yield break;

        int WriteHead(OggOpusWriter writer, OpusHead opusHead)
        {
            if (!writer.Write(opusHead))
                throw new InvalidOperationException("Error writing WebM stream. Buffer is too small.");

            return writer.Position;
        }

        int WriteTags(OggOpusWriter writer, OpusTags opusTags)
        {
            if (!writer.Write(opusTags))
                throw new InvalidOperationException("Error writing WebM stream. Buffer is too small.");

            return writer.Position;
        }

        int WriteFrame(OggOpusWriter writer, IReadOnlyCollection<AudioFrame> audioFrames)
        {
            if (!writer.Write(audioFrames))
                throw new InvalidOperationException("Error writing WebM stream. Buffer is too small.");

            return writer.Position;
        }
    }
}
