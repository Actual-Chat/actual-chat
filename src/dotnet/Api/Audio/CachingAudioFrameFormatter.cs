using System.Buffers;
using MessagePack.Formatters;

namespace ActualChat.Audio;

#pragma warning disable CS0618 // Type or member is obsolete

/// <summary>
/// MessagePack formatter for <see cref="AudioFrame"/> that enables serialize-once fan-out.
/// Hand-written — bypasses the auto-generated formatter to eliminate per-frame Gen0 allocation
/// of a separate <c>byte[]</c> for <see cref="MediaFrame.Data"/> and the dynamic-IL machinery
/// behind the default formatter. Mirrors <see cref="ActualChat.Video.CachingVideoFrameFormatter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Serialize:</b> if <see cref="AudioFrame.SerializedData"/> is populated, the cached bytes
/// are written via <see cref="MessagePackWriter.WriteRaw(ReadOnlySpan{byte})"/> — zero-cost.
/// On cache miss the frame is encoded into a pooled <see cref="ArrayPoolBuffer{T}"/> scratch
/// buffer, the final bytes are copied to a plain <c>byte[]</c> held by the frame.
/// </para>
/// <para>
/// <b>Deserialize:</b> the raw MessagePack bytes for the frame are copied into a plain
/// <c>byte[]</c> owned by the frame. The frame is parsed out of that copy, so
/// <see cref="MediaFrame.Data"/> is a slice into the same <c>byte[]</c>. Lifetime is purely
/// GC-driven: the array lives as long as any consumer holds the frame. No pooling, no Dispose.
/// </para>
/// <para>
/// Bound to <see cref="AudioFrame"/> via <c>[MessagePackFormatter]</c> on the type —
/// AttributeFormatterResolver instantiates this formatter automatically.
/// MessagePack's built-in <c>ArrayFormatter&lt;AudioFrame&gt;</c> automatically delegates each
/// element to this formatter, so <see cref="AudioFrame"/>[] batches hit the cache too.
/// </para>
/// <para>
/// Wire format: a 4-entry MessagePack map with PascalCase string keys — Data (bin),
/// Offset (int64 ticks), Duration (int64 ticks), IsKeyFrame (bool).
/// </para>
/// </remarks>
public sealed class CachingAudioFrameFormatter : IMessagePackFormatter<AudioFrame?>
{
    public static readonly CachingAudioFrameFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, AudioFrame? value, MessagePackSerializerOptions options)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }

        var cached = value.SerializedData;
        if (!cached.IsEmpty) {
            writer.WriteRaw(cached.Span);
            return;
        }

        EnsureSerializedData(value);
        writer.WriteRaw(value.SerializedData.Span);
    }

    public AudioFrame? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;

        // Mark the start, then Skip() the entire frame map to find its end.
        var startPos = reader.Position;
        reader.Skip();
        var slice = reader.Sequence.Slice(startPos, reader.Position);
        var len = (int)slice.Length;

        // Plain GC-managed byte[] — not pooled. Exact size (no bucket rounding slack).
        var bytes = new byte[len];
        slice.CopyTo(bytes);
        return ParseFrame(bytes);
    }

    // --- private ---

    private static AudioFrame ParseFrame(byte[] bytes)
    {
        var reader = new MessagePackReader(bytes);
        var mapLen = reader.ReadMapHeader();

        long offsetTicks = 0;
        long durationTicks = Constants.Audio.OpusFrameDuration.Ticks;
        var isKey = true;
        var dataSlice = default(ReadOnlyMemory<byte>);

        for (var i = 0; i < mapLen; i++) {
            var key = reader.ReadString();
            switch (key) {
                case "Data":
                    dataSlice = ReadBinSlice(ref reader);
                    break;
                case "Offset":
                    offsetTicks = ReadInt64Compatible(ref reader);
                    break;
                case "Duration":
                    durationTicks = ReadInt64Compatible(ref reader);
                    break;
                case "IsKeyFrame":
                    isKey = reader.ReadBoolean();
                    break;
                default:
                    // Forward-compat: tolerate unknown fields.
                    reader.Skip();
                    break;
            }
        }

        return new AudioFrame {
            Data = dataSlice,                       // slice of bytes
            Offset = new TimeSpan(offsetTicks),
            Duration = new TimeSpan(durationTicks),
            LegacyIsKeyFrame = isKey,
            SerializedData = bytes,
        };
    }

    // Returns a ReadOnlyMemory that is a direct slice of the underlying single-segment buffer.
    private static long ReadInt64Compatible(ref MessagePackReader reader)
    {
        if (reader.NextMessagePackType != MessagePackType.Float)
            return reader.ReadInt64();

        var value = reader.ReadDouble();
        var ticks = checked((long)value);
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (ticks != value)
            throw new MessagePackSerializationException($"Expected an integer TimeSpan tick value, got {value}.");

        return ticks;
    }

    private static ReadOnlyMemory<byte> ReadBinSlice(ref MessagePackReader reader)
    {
        var seq = reader.ReadBytes();
        return seq is { } s ? s.First : default;
    }

    private static void EnsureSerializedData(AudioFrame frame)
    {
        if (!frame.SerializedData.IsEmpty)
            return;

        // Scratch pooled buffer during serialize, final bytes live on a plain byte[].
        var scratch = new ArrayPoolBuffer<byte>(frame.Data.Length + 64, mustClear: false);
        byte[] bytes;
        try {
            var writer = new MessagePackWriter(scratch);
            WriteFrame(ref writer, frame);
            writer.Flush();
            bytes = scratch.WrittenSpan.ToArray();
        }
        finally {
            scratch.Dispose();
        }

        // Single-writer by design — SerializedData is populated at ingress (RPC receive loop).
        if (frame.SerializedData.IsEmpty)
            frame.SerializedData = bytes;
    }

    private static void WriteFrame(ref MessagePackWriter writer, AudioFrame v)
    {
        writer.WriteMapHeader(4);

        writer.Write("Data");
        writer.Write(v.Data.Span);

        writer.Write("Offset");
        writer.Write(v.Offset.Ticks);

        writer.Write("Duration");
        writer.Write(v.Duration.Ticks);

        writer.Write("IsKeyFrame");
        writer.Write(v.LegacyIsKeyFrame);
    }
}
