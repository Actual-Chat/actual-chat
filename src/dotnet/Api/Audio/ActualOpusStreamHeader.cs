using System.Buffers;
using ActualChat.Spans;

namespace ActualChat.Audio;

public record ActualOpusStreamHeader(Moment CreatedAt, AudioFormat Format)
{
    public static readonly byte[] Prefix = { 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53 }; // A_OPUS_S
    public static readonly byte[] ActualOpusStreamFormat = { 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53, 0x03 }; // A_OPUS_S + version = 3

    public static ActualOpusStreamHeader Parse(ref ReadOnlySequence<byte> sequence)
    {
        var headerPrefixLength = Prefix.Length;
        Span<byte> buffer = stackalloc byte[headerPrefixLength + 1 + 2 + 8];
        sequence.Slice(0, headerPrefixLength + + 1 + 2 + 8).CopyTo(buffer);
        if (!buffer.StartsWith(Prefix))
            throw new InvalidOperationException("Actual Opus stream header is invalid.");

        var version = buffer[headerPrefixLength];
        if (version is <= 0 or > 3)
            throw new NotSupportedException($"Actual Opus stream version is invalid - ${version}. Only version 1-2 is supported.");

        var format = AudioSource.DefaultFormat;
        var createdAt = MomentClockSet.Default.SystemClock.Now;
        if (version == 2) {
            var reader = new SpanReader(buffer[(headerPrefixLength + 1)..]);
            var skip = reader.ReadInt16();
            if (!skip.HasValue)
                throw new InvalidOperationException("Unable to read PreSkipFrames.");

            format = format with { PreSkipFrames = skip.Value };
            sequence = sequence.Slice(headerPrefixLength + 1 + 2);
        }
        else if (version == 3) {
            var reader = new SpanReader(buffer[(headerPrefixLength + 1)..]);
            var skip = reader.ReadInt16();
            if (!skip.HasValue)
                throw new InvalidOperationException("Unable to read PreSkipFrames.");

            var createdAtTicks = reader.ReadLong();
            if (!createdAtTicks.HasValue)
                throw new InvalidOperationException("Unable to read CreatedAt ticks.");

            createdAt = new Moment(createdAtTicks.Value);
            format = format with { PreSkipFrames = skip.Value };
            sequence = sequence.Slice(headerPrefixLength + 1 + 2 + 8);
        }
        else
            sequence = sequence.Slice(headerPrefixLength + 1);

        return new ActualOpusStreamHeader(createdAt, format);
    }

    public byte[] Serialize()
    {
        var header = new byte[ActualOpusStreamFormat.Length + 2 + 8];
        var spanWriter = new SpanWriter(header);
        spanWriter.Write(ActualOpusStreamFormat, ActualOpusStreamFormat.Length);
        spanWriter.Write((ushort)Format.PreSkipFrames);
        spanWriter.Write(CreatedAt.EpochOffsetTicks);
        return header;
    }
}
